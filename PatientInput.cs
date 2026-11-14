using System.Globalization;

internal sealed record Patient(
    string FullName, string Sex, int Age, string? Allergies, string? Medications, string? History,
    string Pregnancy, string? PregnancyDetails, string? Passport, string? Policy, DateTimeOffset UpdatedAt);

internal sealed record FieldError(string Field, string Message);

internal static class PatientInput
{
    private static readonly (string Field, int Limit, string Label)[] TextFields =
    [
        ("allergies", 1500, "аллергические реакции"),
        ("medications", 1500, "постоянно принимаемые лекарства"),
        ("history", 2500, "анамнез"),
        ("pregnancyDetails", 500, "примечание о беременности")
    ];

    public static (Patient? Patient, FieldError? Error) Parse(IFormCollection form)
    {
        string? Value(string field)
        {
            var text = form[field].ToString().Trim();
            return text.Length == 0 ? null : text;
        }

        static (Patient?, FieldError) Invalid(string field, string message) => (null, new(field, message));

        var name = Value("fullName");
        if (name is null || name.Length is < 2 or > 160)
            return Invalid("fullName", "Введите ФИО от 2 до 160 символов.");

        var sex = Value("sex");
        if (sex is not ("female" or "male" or "unknown"))
            return Invalid("sex", "Выберите пол.");

        if (!int.TryParse(Value("age"), NumberStyles.None, CultureInfo.InvariantCulture, out var age) || age > 125)
            return Invalid("age", "Укажите возраст целым числом от 0 до 125.");

        foreach (var (field, limit, label) in TextFields)
        {
            if (Value(field)?.Length > limit)
                return Invalid(field, $"Сократите поле «{label}» до {limit} символов.");
        }

        var pregnancy = Value("pregnancy") ?? "unknown";
        if (pregnancy is not ("yes" or "no" or "unknown" or "na"))
            return Invalid("pregnancy", "Выберите значение для поля «Беременность».");

        var passport = Value("passport");
        if (passport is not null && !HasDigits(passport, 10))
            return Invalid("passport", "Введите 4 цифры серии и 6 цифр номера паспорта РФ.");

        var policy = Value("policy");
        if (policy is not null && (policy.Length > 40 || !policy.Any(char.IsAsciiDigit) ||
            policy.Any(character => !char.IsLetterOrDigit(character) && character is not (' ' or '-'))))
            return Invalid("policy", "Введите номер ОМС. Допустимы буквы, цифры, пробелы и дефисы.");

        if (Value("consent") != "yes")
            return Invalid("consent", "Подтвердите доступ к медицинским сведениям по ссылке.");

        return (new Patient(name, sex, age, Value("allergies"), Value("medications"), Value("history"),
            pregnancy, pregnancy == "yes" ? Value("pregnancyDetails") : null, passport, policy, DateTimeOffset.UtcNow), null);
    }

    private static bool HasDigits(string value, int count) =>
        value.Count(char.IsAsciiDigit) == count && value.All(character => char.IsAsciiDigit(character) || character == ' ');

    public static async Task<(string? ContentType, byte[]? Bytes, string? Error)> ReadPhoto(
        IFormFile? file, CancellationToken cancellation)
    {
        if (file is null) return (null, null, null);
        if (file.Length is 0 or > 5_000_000) return (null, null, "Размер фотографии должен быть от 1 байта до 5 МБ.");

        using var stream = new MemoryStream((int)file.Length);
        await file.CopyToAsync(stream, cancellation);
        var bytes = stream.ToArray();
        var type = DetectImage(bytes);
        return type is null ? (null, null, "Выберите фотографию в формате JPG, PNG или WebP.") : (type, bytes, null);
    }

    private static string? DetectImage(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 4 && bytes.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF })) return "image/jpeg";
        if (bytes.Length >= 24 && bytes.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) &&
            bytes[12..16].SequenceEqual("IHDR"u8)) return "image/png";
        if (bytes.Length >= 16 && bytes.StartsWith("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8)) return "image/webp";
        return null;
    }
}
