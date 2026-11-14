# ExtraMed

Экстренная карточка пациента на ASP.NET Core и C# (.NET 10). Форма принимает фотографию, ФИО, пол, возраст, аллергические реакции, постоянные лекарства, заболевания, операции, хронические патологии, сведения о беременности, серию и номер паспорта РФ и номер полиса ОМС.

## Локальная проверка

Из корня проекта запустите `dotnet run` и откройте [http://localhost:5150](http://localhost:5150). Заполните анкету вымышленными данными, нажмите «Создать карточку», затем «Открыть карточку».

Для проверки формы с паспортом и ОМС задайте пароль сотрудника перед запуском. Для записи на NFC дополнительно нужен HTTPS-адрес, доступный с телефона.

## Развёртывание на Vercel

Проект использует [Container Runtime](https://vercel.com/docs/functions/runtimes/container) для запуска существующего сервера ASP.NET Core. `vercel.json` выбирает **Container**, а корневой `Dockerfile` собирает и запускает .NET 10. Сайт и API работают на одном HTTPS-домене.

1. В Vercel импортируйте Git-репозиторий ExtraMed. Выберите корень репозитория и убедитесь, что выбран Framework Preset **Container**.
2. Откройте **Storage**, создайте **Private Blob** и подключите его к проекту в окружении **Production**. При подключении включите **Add a read-write token env var**, чтобы Vercel добавил `BLOB_READ_WRITE_TOKEN`.
3. В **Settings → Environment Variables** добавьте переменные для **Production**.

   | Переменная | Значение |
   | --- | --- |
   | `ExtraMed__VercelEncryptionKey` | Случайные 32 байта в Base64. Создайте значение один раз командой `node -p "require('node:crypto').randomBytes(32).toString('base64')"`. Сохраните ключ в менеджере секретов и используйте тот же ключ при будущих развёртываниях. |
   | `ExtraMed__StaffPassword` | Общий пароль сотрудника длиной 16–256 символов. Он нужен для сохранения и просмотра паспорта и ОМС. |
   | `ExtraMed__PublicBaseUrl` | Постоянный HTTPS-адрес сайта без пути, например `https://extra-med.vercel.app`. При включённых системных переменных Vercel приложение подставляет production-домен автоматически. Задайте адрес явно, если используете определённый домен для NFC. |

4. Выполните **Deploy** или **Redeploy**, если первый запуск был до настройки переменных. Для NFC используйте постоянный production-домен, доступный с телефона без входа в Vercel. После запуска проверьте создание карточки, вход сотрудника и NFC на вымышленных данных.

Анкеты и фотографии хранятся в приватном Blob в зашифрованном виде. Для чтения после обновления нужны тот же Blob и тот же ключ. Локальные анкеты из `App_Data` автоматически в Vercel не переносятся. Если нужен Preview, подключите к этому окружению отдельный Private Blob и задайте отдельные пароль и ключ. Ссылки для NFC по умолчанию выдаются только в Production.

В Vercel фотография может занимать до 4 МБ с учётом [лимита тела запроса](https://vercel.com/docs/functions/limitations#request-body-size). При локальном запуске лимит фото составляет 5 МБ. Если на Vercel задана переменная `PORT`, приложение будет слушать указанный порт. По умолчанию контейнер слушает порт 80.

## Настройка на другом .NET-хостинге

Запустите приложение на сервере с .NET 10 и настройте HTTPS для домена или адреса, открывающегося на телефоне. Для публикации приложения соберите его командой `dotnet publish -c Release`. При запуске используйте каталог публикации как рабочий каталог. Если HTTPS завершается на прокси, он должен передавать `X-Forwarded-Proto` и `X-Forwarded-For`. Приложение по умолчанию доверяет этим заголовкам от прокси на loopback. Если прокси на другом узле, настройте его адрес в `ForwardedHeadersOptions`.

Настройки передаются через окружение.

| Переменная | Значение |
| --- | --- |
| `ExtraMed__PublicBaseUrl` | HTTPS-адрес без пути, например `https://med.example.org`. Из него формируются ссылки для меток. |
| `ExtraMed__StaffPassword` | Пароль длиной 16–256 символов. В этой версии это общий пароль сотрудников. До его настройки сохранение паспорта и ОМС недоступно. |
| `ExtraMed__DataDirectory` | Постоянный каталог для анкет и ключей, по умолчанию `App_Data`. Выдайте доступ к нему только учётной записи приложения и администраторам сервера. При резервном копировании сохраняйте каталог анкет и ключи вместе. |

На Android откройте сайт в Chrome, включите NFC, сохраните анкету и нажмите «Записать NFC-метку». Поднесите смартфон к метке, доступной для записи. В других браузерах можно скопировать ссылку и записать её через NFC-приложение как запись NDEF типа URL. Повторная запись заменит содержимое метки. После записи считайте её телефоном и убедитесь, что открывается нужная карточка.

Метка содержит URL со случайным идентификатором карточки. Для просмотра нужен интернет или доступ к сети, в которой размещён сайт. Сведения и фото доступны каждому, у кого есть ссылка. Для доступа к паспорту и ОМС требуется вход сотрудника. Карточка показывает дату заполнения. При изменении данных создайте новую карточку и перезапишите метку.

[Web NFC в Chrome для Android](https://developer.chrome.com/docs/capabilities/nfc) · [Фоновое чтение NFC на iPhone](https://developer.apple.com/documentation/CoreNFC/adding-support-for-background-tag-reading)

<details>
<summary>Иллюстрация NFC-метки</summary>

Файл `wwwroot/images/nfc-shirt.jpg` создан встроенным ImageGen. Запрос к генератору:

> Create a photorealistic editorial product photograph for the right-hand hero of a Russian emergency medical NFC card website. Very wide landscape with tightly cropped subject, intended to be displayed at about 620 by 330. On the left half in the foreground, a real hand holds a modern charcoal smartphone diagonally from bottom left toward upper middle, back of the phone toward camera, very near the chest of a person wearing a sand beige cotton crew-neck shirt at the right. A small square muted dark crimson fabric label with a simple off-white contactless NFC arcs symbol is neatly sewn on the chest near the center-right, fully visible next to the top half of the smartphone. Person is cropped below the chin, only neck and shirt, no face. Authentic cloth weave and seam, realistic hands, subtle film grain, muted desaturated tones, warm low-key natural side lighting with shadows, espresso brown background, a soft dark vignette. Place the objects at the center, away from edges. Sophisticated commercial healthcare product photo, cinematic, warm rather than orange. No text, no lettering, no border, no logos, no extra objects, no digital UI.

</details>
