using System.Runtime.CompilerServices;

namespace ForceBond.Launcher;

// English is the source language and fallback. Language affects only the launcher.
internal static class L
{
    public static string Language { get; set; } = "en";
    private static readonly ConditionalWeakTable<Control, Binding> Bindings = new();
    private sealed class Binding
    {
        public string Text = "", Name = "", Description = "";
        public bool Updating;
    }

    public static string T(string text)
    {
        if (Language == "en" || string.IsNullOrEmpty(text)) return text;
        if (Strings.TryGetValue(text, out var pair)) return Language == "uk" ? pair.Uk : pair.Ru;
        foreach (var entry in Strings)
        {
            int marker = entry.Key.IndexOf("{0}", StringComparison.Ordinal);
            if (marker < 0) continue;
            string prefix = entry.Key[..marker], suffix = entry.Key[(marker + 3)..];
            if (text.Length < prefix.Length + suffix.Length || !text.StartsWith(prefix, StringComparison.Ordinal) || !text.EndsWith(suffix, StringComparison.Ordinal)) continue;
            string value = text.Substring(prefix.Length, text.Length - prefix.Length - suffix.Length);
            return (Language == "uk" ? entry.Value.Uk : entry.Value.Ru).Replace("{0}", T(value), StringComparison.Ordinal);
        }
        return text;
    }

    public static void Apply(Control root)
    {
        if (!Bindings.TryGetValue(root, out var binding))
        {
            binding = new Binding { Text = root.Text, Name = root.AccessibleName ?? "", Description = root.AccessibleDescription ?? "" };
            Bindings.Add(root, binding);
            root.TextChanged += (_, _) =>
            {
                if (binding.Updating) return;
                binding.Text = root.Text;
                Update(root, binding);
            };
        }
        Update(root, binding);
        foreach (Control child in root.Controls) Apply(child);
        root.Invalidate();
    }

    private static void Update(Control control, Binding binding)
    {
        binding.Updating = true;
        try
        {
            control.Text = T(binding.Text);
            if (binding.Text == "EXPLORE THE GALAXY.\nTOGETHER.")
            {
                float size = Language == "en" ? 25f : 20f;
                if (Math.Abs(control.Font.Size - size) > .1f)
                    control.Font = new Font("Segoe UI Semibold", size, FontStyle.Bold);
            }
            if (binding.Name.Length > 0) control.AccessibleName = T(binding.Name);
            if (binding.Description.Length > 0) control.AccessibleDescription = T(binding.Description);
        }
        finally { binding.Updating = false; }
    }

    private static readonly Dictionary<string, (string Uk, string Ru)> Strings = new(StringComparer.Ordinal)
    {
        ["RETRY ACCESS CHECK"] = ("ПОВТОРИТИ ПЕРЕВІРКУ", "ПОВТОРИТЬ ПРОВЕРКУ"),
        ["Discord sign-in timed out. Click Sign in with Discord to try again."] = ("Час входу через Discord вичерпано. Натисніть Вхід через Discord, щоб повторити.", "Время входа через Discord истекло. Нажмите Войти через Discord, чтобы повторить."),
        ["Your sign-in is saved, but the access check timed out. Click Retry access check."] = ("Вхід збережено, але час перевірки доступу вичерпано. Повторіть перевірку доступу.", "Вход сохранён, но время проверки доступа истекло. Повторите проверку доступа."),
        ["The access check timed out. Click Retry access check."] = ("Час перевірки доступу вичерпано. Повторіть перевірку доступу.", "Время проверки доступа истекло. Повторите проверку доступа."),
        ["Could not verify access. Click Retry access check. Installed builds still work."] = ("Не вдалося перевірити доступ. Повторіть перевірку. Встановлені збірки працюють.", "Не удалось проверить доступ. Повторите проверку. Установленные сборки работают."),
        ["Engine not installed. Open Beta Access, sign in and check for updates to download it."] = ("Рушій не встановлено. Відкрийте Beta Access, увійдіть і перевірте оновлення для завантаження.", "Движок не установлен. Откройте Beta Access, войдите и проверьте обновления для скачивания."),
        ["WAITING FOR PLAYERS"] = ("ОЧІКУВАННЯ ГРАВЦІВ", "ОЖИДАНИЕ ИГРОКОВ"),
        ["PLAYERS ARE CONNECTING"] = ("ГРАВЦІ ПІДКЛЮЧАЮТЬСЯ", "ИГРОКИ ПОДКЛЮЧАЮТСЯ"),
        ["Finish or skip the prologue to begin co-op."] = ("Завершіть або пропустіть пролог, щоб почати кооператив.", "Завершите или пропустите пролог, чтобы начать кооператив."),
        ["PROLOGUE / CO-OP WAITING"] = ("ПРОЛОГ / ОЧІКУВАННЯ КООПЕРАТИВУ", "ПРОЛОГ / ОЖИДАНИЕ КООПЕРАТИВА"),
        ["TESTING"] = ("ТЕСТУВАННЯ", "ТЕСТИРОВАНИЕ"),
        ["Load your character or a save. Steam hosting starts when the world is ready."] = ("Завантажте персонажа чи збереження. Steam-сесія створиться, коли світ буде готовий.", "Загрузите персонажа или сохранение. Steam-сессия создастся, когда мир будет готов."),
        ["Load a character to begin a Steam session."] = ("Завантажте персонажа для Steam-сесії.", "Загрузите персонажа для Steam-сессии."),
        ["World ready. Invite friends through Steam (F8)."] = ("Світ готовий. Запрошуйте друзів через Steam (F8).", "Мир готов. Приглашайте друзей через Steam (F8)."),
        ["Players present in the host's world."] = ("Гравці у світі хоста.", "Игроки находятся в мире хоста."),
        ["STEAM SESSION READY"] = ("STEAM-СЕСІЯ ГОТОВА", "STEAM-СЕССИЯ ГОТОВА"),
        ["CREATING STEAM SESSION"] = ("СТВОРЕННЯ STEAM-СЕСІЇ", "СОЗДАНИЕ STEAM-СЕССИИ"),
        ["PLAYERS IN WORLD"] = ("ГРАВЦІ У СВІТІ", "ИГРОКИ В МИРЕ"),
        ["Selected mode"] = ("Обраний режим", "Выбранный режим"),
        ["Session mode"] = ("Режим сесії", "Режим сессии"),
        ["Up to 8 players per session"] = ("До 8 гравців у сесії", "До 8 игроков в сессии"),
        ["Own characters · Personal inventory"] = ("Власні персонажі · Особистий інвентар", "Свои персонажи · Личный инвентарь"),
        ["Host's companions · Shared inventory"] = ("Супутники хоста · Спільний інвентар", "Спутники хоста · Общий инвентарь"),
        ["STORY CO-OP"] = ("СЮЖЕТНИЙ КООПЕРАТИВ", "СЮЖЕТНЫЙ КООПЕРАТИВ"),
        ["Up to 3 players per session"] = ("До 3 гравців у сесії", "До 3 игроков в сессии"),
        ["ONE PARTY. ONE GALACTIC JOURNEY."] = ("ОДНА КОМАНДА. СПІЛЬНА ГАЛАКТИЧНА ПОДОРОЖ.", "ОДНА КОМАНДА. ОБЩЕЕ ГАЛАКТИЧЕСКОЕ ПУТЕШЕСТВИЕ."),
        ["Force Bond is publicly available. No Discord sign-in is required to download or play."] = ("Force Bond доступний усім. Вхід через Discord для завантаження та гри не потрібен.", "Force Bond доступен всем. Вход через Discord для скачивания и игры не требуется."),
        ["BETA ACCESS"] = ("БЕТА-ДОСТУП", "БЕТА-ДОСТУП"),
        ["Closed beta access"] = ("Доступ до закритої бети", "Доступ к закрытой бете"),
        ["Checking access..."] = ("Перевірка доступу...", "Проверка доступа..."),
        ["SIGN IN WITH DISCORD"] = ("УВІЙТИ ЧЕРЕЗ DISCORD", "ВОЙТИ ЧЕРЕЗ DISCORD"),
        ["SIGN OUT"] = ("ВИЙТИ", "ВЫЙТИ"),
        ["CHECK FOR UPDATES"] = ("ПЕРЕВІРИТИ ОНОВЛЕННЯ", "ПРОВЕРИТЬ ОБНОВЛЕНИЯ"),
        ["The access service is not connected yet."] = ("Сервіс доступу ще не підключено.", "Сервис доступа ещё не подключён."),
        ["Public access. Discord sign-in is not required."] = ("Публічний доступ. Вхід через Discord не потрібен.", "Публичный доступ. Вход через Discord не требуется."),
        ["Beta access granted. Downloads and updates are available."] = ("Бета-доступ підтверджено. Завантаження й оновлення доступні.", "Бета-доступ подтверждён. Скачивание и обновления доступны."),
        ["Sign in with Discord to check beta access."] = ("Увійдіть через Discord для перевірки бета-доступу.", "Войдите через Discord для проверки бета-доступа."),
        ["Access requires the Playtester or Syndicate Corp. role on our Discord server."] = ("Для доступу потрібна роль Playtester або Syndicate Corp. на нашому Discord-сервері.", "Для доступа нужна роль Playtester или Syndicate Corp. на нашем Discord-сервере."),
        ["Complete sign-in in your browser, then return here."] = ("Завершіть вхід у браузері та поверніться сюди.", "Завершите вход в браузере и вернитесь сюда."),
        ["Sign-in cancelled or timed out. Installed builds still work."] = ("Вхід скасовано або час вичерпано. Встановлені збірки працюють.", "Вход отменён или время истекло. Установленные сборки работают."),
        ["Could not verify access. Try again later. Installed builds still work."] = ("Не вдалося перевірити доступ. Спробуйте пізніше. Встановлені збірки працюють.", "Не удалось проверить доступ. Попробуйте позже. Установленные сборки работают."),
        ["Checking for updates..."] = ("Перевірка оновлень...", "Проверка обновлений..."),
        ["Close KOTOR II to verify the updated mod files."] = ("Закрийте KOTOR II для перевірки оновлених файлів мода.", "Закройте KOTOR II для проверки обновлённых файлов мода."),
        ["Verifying updated mod files..."] = ("Перевірка оновлених файлів мода...", "Проверка обновлённых файлов мода..."),
        ["Game files verified. Ready to launch."] = ("Файли гри перевірено. Можна запускати.", "Файлы игры проверены. Можно запускать."),
        ["Game file verification failed. Open Settings to retry."] = ("Не вдалося перевірити файли гри. Повторіть у налаштуваннях.", "Не удалось проверить файлы игры. Повторите в настройках."),
        ["Force Bond update"] = ("Оновлення Force Bond", "Обновление Force Bond"),
        ["FORCE BOND UPDATE"] = ("ОНОВЛЕННЯ FORCE BOND", "ОБНОВЛЕНИЕ FORCE BOND"),
        ["LATER"] = ("ПІЗНІШЕ", "ПОЗЖЕ"),
        ["Update"] = ("Оновити", "Обновить"),
        ["Open project website"] = ("Відкрити сайт проєкту", "Открыть сайт проекта"),
        ["DOWNLOAD & INSTALL"] = ("ЗАВАНТАЖИТИ Й ВСТАНОВИТИ", "СКАЧАТЬ И УСТАНОВИТЬ"),
        ["Close KOTOR II before installing. The previous build is kept."] = ("Закрийте KOTOR II перед установленням. Попередню збірку буде збережено.", "Закройте KOTOR II перед установкой. Предыдущая сборка сохранится."),
        ["Close KOTOR II before installing updates."] = ("Закрийте KOTOR II перед установленням оновлень.", "Закройте KOTOR II перед установкой обновлений."),
        ["Downloading update..."] = ("Завантаження оновлення...", "Скачивание обновления..."),
        ["Download cancelled. Your current build is unchanged."] = ("Завантаження скасовано. Поточна збірка не змінилася.", "Скачивание отменено. Текущая сборка не изменена."),
        ["Update failed. Your current build is unchanged."] = ("Не вдалося оновити. Поточна збірка не змінилася.", "Обновление не удалось. Текущая сборка не изменена."),
        ["PATCH LOG"] = ("ІСТОРІЯ ОНОВЛЕНЬ", "ИСТОРИЯ ИЗМЕНЕНИЙ"),
        ["CURRENT RELEASE"] = ("ПОТОЧНА ВЕРСІЯ", "ТЕКУЩАЯ ВЕРСИЯ"),
        ["ADDED"] = ("ДОДАНО", "ДОБАВЛЕНО"),
        ["FIXED"] = ("ВИПРАВЛЕНО", "ИСПРАВЛЕНО"),
        ["IMPROVED"] = ("ПОКРАЩЕНО", "УЛУЧШЕНО"),
        ["Release notes unavailable."] = ("Історія оновлень недоступна.", "История изменений недоступна."),
        ["Waiting for a partner"] = ("Очікуємо партнера", "Ожидание партнёра"),
        ["STEAM FRIENDS"] = ("ДРУЗІ STEAM", "ДРУЗЬЯ STEAM"),
        ["READY FOR YOUR NEXT JOURNEY"] = ("ГОТОВІ ДО НОВОЇ ПОДОРОЖІ", "ГОТОВЫ К НОВОМУ ПУТЕШЕСТВИЮ"),
        ["CONNECTING FORCE BOND"] = ("ПІДКЛЮЧЕННЯ FORCE BOND", "ПОДКЛЮЧЕНИЕ FORCE BOND"),
        ["PREPARING YOUR WORLD"] = ("ПІДГОТОВКА ІГРОВОГО СВІТУ", "ПОДГОТОВКА ИГРОВОГО МИРА"),
        ["WAITING FOR A PARTNER"] = ("ОЧІКУЄМО ПАРТНЕРА", "ОЖИДАНИЕ ПАРТНЁРА"),
        ["PARTNER IS CONNECTING"] = ("ПАРТНЕР ПІДКЛЮЧАЄТЬСЯ", "ПАРТНЁР ПОДКЛЮЧАЕТСЯ"),
        ["THE FORCE BOND IS ACTIVE"] = ("ЗВ’ЯЗОК СИЛИ ВСТАНОВЛЕНО", "СВЯЗЬ СИЛЫ УСТАНОВЛЕНА"),
        ["YOUR CO-OP PARTNER"] = ("ВАШ ПАРТНЕР", "ВАШ ПАРТНЁР"),
        ["Synchronized"] = ("Синхронізовано", "Синхронизировано"),
        ["Connected · synchronizing"] = ("Підключено · синхронізація", "Подключён · синхронизация"),
        ["Invite a friend through Steam"] = ("Запросіть друга через Steam", "Пригласите друга через Steam"),
        ["No location loaded"] = ("Локацію не завантажено", "Локация не загружена"),
        ["Peragus · Administration Level"] = ("Перагус · адміністративний рівень", "Перагус · административный уровень"),
        ["Peragus · Mining Tunnels"] = ("Перагус · шахтні тунелі", "Перагус · шахтные туннели"),
        ["Peragus · Fuel Depot"] = ("Перагус · паливний склад", "Перагус · топливный склад"),
        ["Peragus · Asteroid Exterior"] = ("Перагус · поверхня астероїда", "Перагус · поверхность астероида"),
        ["Peragus · Hangar"] = ("Перагус · ангар", "Перагус · ангар"),
        ["Harbinger"] = ("Провісник", "Предвестник"),
        ["Telos"] = ("Телос", "Телос"),
        ["Nar Shaddaa"] = ("Нар-Шаддаа", "Нар-Шаддаа"),
        ["Dantooine"] = ("Дантуїн", "Дантуин"),
        ["Korriban"] = ("Коррібан", "Коррибан"),
        ["Dxun"] = ("Дксун", "Дксун"),
        ["Onderon"] = ("Ондерон", "Ондерон"),
        ["Ebon Hawk"] = ("Ебеновий Яструб", "Эбеновый ястреб"),
        ["Overhead chat distance"] = ("Відстань тексту над головою", "Дальность текста над головой"),
        ["Off"] = ("Вимк.", "Выкл."),
        ["MINIMIZE"] = ("ЗГОРНУТИ", "СВЕРНУТЬ"),
        ["STEAM CO-OP"] = ("КООПЕРАТИВ STEAM", "КООПЕРАТИВ STEAM"),
        ["CLOSE  ×"] = ("ЗАКРИТИ ×", "ЗАКРЫТЬ ×"),
        ["Close Force Bond launcher"] = ("Закрити лаунчер Force Bond", "Закрыть лаунчер Force Bond"),
        ["Minimize Force Bond launcher"] = ("Згорнути лаунчер Force Bond", "Свернуть лаунчер Force Bond"),
        ["Close {0}"] = ("Закрити: {0}", "Закрыть: {0}"),
        ["EXPLORE THE GALAXY.\nTOGETHER."] = ("ВІДКРИВАЙТЕ ГАЛАКТИКУ.\nРАЗОМ.", "ОТКРЫВАЙТЕ ГАЛАКТИКУ.\nВМЕСТЕ."),
        ["GAME STATUS"] = ("СТАН ГРИ", "СОСТОЯНИЕ ИГРЫ"),
        ["STEAM CLIENT"] = ("КЛІЄНТ STEAM", "КЛИЕНТ STEAM"),
        ["GAME INSTALL"] = ("ВСТАНОВЛЕННЯ ГРИ", "УСТАНОВКА ИГРЫ"),
        ["CHECKING"] = ("ПЕРЕВІРКА", "ПРОВЕРКА"),
        ["STEAM RUNNING"] = ("STEAM ЗАПУЩЕНО", "STEAM ЗАПУЩЕН"),
        ["STEAM INSTALLED / CLOSED"] = ("STEAM ВСТАНОВЛЕНО / ЗАКРИТО", "STEAM УСТАНОВЛЕН / ЗАКРЫТ"),
        ["STEAM NOT FOUND"] = ("STEAM НЕ ЗНАЙДЕНО", "STEAM НЕ НАЙДЕН"),
        ["KOTOR II FOUND"] = ("KOTOR II ЗНАЙДЕНО", "KOTOR II НАЙДЕНА"),
        ["SELECT GAME FOLDER"] = ("ВИБЕРІТЬ ПАПКУ ГРИ", "ВЫБЕРИТЕ ПАПКУ ИГРЫ"),
        ["CHECK INSTALLATION"] = ("ПЕРЕВІРТЕ ВСТАНОВЛЕННЯ", "ПРОВЕРЬТЕ УСТАНОВКУ"),
        ["Game folder not detected"] = ("Папку гри не знайдено", "Папка игры не найдена"),
        ["Use Change Game Folder to locate swkotor2.exe"] = ("Виберіть папку, що містить swkotor2.exe", "Выберите папку с файлом swkotor2.exe"),
        ["LAUNCH FORCE BOND"] = ("ЗАПУСТИТИ FORCE BOND", "ЗАПУСТИТЬ FORCE BOND"),
        ["CONNECT FORCE BOND"] = ("ПІДКЛЮЧИТИ FORCE BOND", "ПОДКЛЮЧИТЬ FORCE BOND"),
        ["GAME RUNNING"] = ("ГРУ ЗАПУЩЕНО", "ИГРА ЗАПУЩЕНА"),
        ["CHANGE GAME FOLDER"] = ("ЗМІНИТИ ПАПКУ ГРИ", "ИЗМЕНИТЬ ПАПКУ ИГРЫ"),
        ["HOW TO CONNECT"] = ("ЯК ПІДКЛЮЧИТИСЯ", "КАК ПОДКЛЮЧИТЬСЯ"),
        ["START THE GAME"] = ("ЗАПУСТІТЬ ГРУ", "ЗАПУСТИТЕ ИГРУ"),
        ["Both players load a save on Peragus - Administration Level."] = ("Обидва гравці завантажують збереження: Перагус — адміністративний рівень.", "Оба игрока загружают сохранение: Перагус — административный уровень."),
        ["INVITE A FRIEND"] = ("ЗАПРОСІТЬ ДРУГА", "ПРИГЛАСИТЕ ДРУГА"),
        ["Invite or join your friend through Steam Friends."] = ("Запросіть друга або приєднайтеся через список друзів Steam.", "Пригласите друга или присоединитесь через список друзей Steam."),
        ["Host: load a character or save to start hosting."] = ("Хост завантажує персонажа або збереження.", "Хост загружает персонажа или сохранение."),
        ["Host: invite your friend through Steam Friends."] = ("Хост запрошує друга через список друзів Steam.", "Хост приглашает друга через список друзей Steam."),
        ["Guest: accept the Steam invite from the main menu."] = ("Гість приймає запрошення Steam у головному меню.", "Гость принимает приглашение Steam в главном меню."),
        ["ROLEPLAY IN DEVELOPMENT"] = ("ROLEPLAY У РОЗРОБЦІ", "ROLEPLAY В РАЗРАБОТКЕ"),
        ["Roleplay is currently unavailable."] = ("Режим поки недоступний.", "Режим пока недоступен."),
        ["HOST: LOAD A SAVE"] = ("ХОСТ: ЗАВАНТАЖТЕ ЗБЕРЕЖЕННЯ", "ХОСТ: ЗАГРУЗИТЕ СОХРАНЕНИЕ"),
        ["Start Force Bond and load your save."] = ("Запустіть Force Bond і завантажте збереження.", "Запустите Force Bond и загрузите сохранение."),
        ["HOST: SEND AN INVITE"] = ("ХОСТ: ЗАПРОСІТЬ ДРУЗІВ", "ХОСТ: ПРИГЛАСИТЕ ДРУЗЕЙ"),
        ["Invite your friends through Steam (F8)."] = ("Запросіть друзів через Steam (F8).", "Пригласите друзей через Steam (F8)."),
        ["GUEST: ACCEPT THE INVITE"] = ("ГІСТЬ: ПРИЙМІТЬ ЗАПРОШЕННЯ", "ГОСТЬ: ПРИМИТЕ ПРИГЛАШЕНИЕ"),
        ["Start Force Bond and accept the Steam invite from the main menu."] = ("Запустіть Force Bond і прийміть запрошення Steam у головному меню.", "Запустите Force Bond и примите приглашение Steam в главном меню."),
        ["OFFICIAL SERVER"] = ("ОФІЦІЙНИЙ СЕРВЕР", "ОФИЦИАЛЬНЫЙ СЕРВЕР"),
        ["From the main menu, choose Force Bond Hard RolePlay to join the official server."] = ("У головному меню виберіть Force Bond Hard RolePlay, щоб увійти на офіційний сервер.", "В главном меню выберите Force Bond Hard RolePlay, чтобы подключиться к официальному серверу."),
        ["BROWSE SERVERS"] = ("СПИСОК СЕРВЕРІВ", "СПИСОК СЕРВЕРОВ"),
        ["Open Server Browser from the main menu, choose a server, and connect."] = ("У головному меню відкрийте Server Browser, виберіть сервер і підключіться.", "В главном меню откройте Server Browser, выберите сервер и подключитесь."),
        ["HOST A SESSION"] = ("СТВОРИТИ СЕСІЮ", "СОЗДАТЬ СЕССИЮ"),
        ["Load your save in Force Bond, then press F8 to invite friends through Steam."] = ("Завантажте збереження у Force Bond і натисніть F8, щоб запросити друзів через Steam.", "Загрузите сохранение в Force Bond и нажмите F8, чтобы пригласить друзей через Steam."),
        ["JOIN A FRIEND"] = ("ПРИЄДНАТИСЯ ДО ДРУГА", "ПОДКЛЮЧИТЬСЯ К ДРУГУ"),
        ["Stay at the Force Bond main menu and accept your friend's Steam invite."] = ("Залишайтеся в головному меню Force Bond і прийміть запрошення друга в Steam.", "Оставайтесь в главном меню Force Bond и примите приглашение друга в Steam."),
        ["OPEN ROLEPLAY"] = ("ВІДКРИЙТЕ ROLEPLAY", "ОТКРОЙТЕ ROLEPLAY"),
        ["Open the Roleplay main menu."] = ("Відкрийте головне меню Roleplay.", "Откройте главное меню Roleplay."),
        ["CHOOSE A SERVER"] = ("ВИБЕРІТЬ СЕРВЕР", "ВЫБЕРИТЕ СЕРВЕР"),
        ["Select a server and connect."] = ("Виберіть сервер і підключіться.", "Выберите сервер и подключитесь."),
        ["OPEN SERVER BROWSER"] = ("ВІДКРИЙТЕ СПИСОК СЕРВЕРІВ", "ОТКРОЙТЕ СПИСОК СЕРВЕРОВ"),
        ["Open Server Browser from the main menu."] = ("Відкрийте Server Browser у головному меню.", "Откройте Server Browser в главном меню."),
        ["JOIN FORCE BOND"] = ("ПРИЄДНАЙТЕСЯ ДО FORCE BOND", "ПОДКЛЮЧИТЕСЬ К FORCE BOND"),
        ["Choose the official Force Bond server."] = ("Виберіть офіційний сервер Force Bond.", "Выберите официальный сервер Force Bond."),
        ["HOST A GAME"] = ("СТВОРІТЬ СЕСІЮ", "СОЗДАЙТЕ СЕССИЮ"),
        ["Host: load your character or save and invite friends through Steam."] = ("Хост завантажує персонажа або збереження й запрошує друзів через Steam.", "Хост загружает персонажа или сохранение и приглашает друзей через Steam."),
        ["JOIN FROM MAIN MENU"] = ("ПРИЄДНАЙТЕСЯ З ГОЛОВНОГО МЕНЮ", "ВОЙДИТЕ ИЗ ГЛАВНОГО МЕНЮ"),
        ["YOUR OWN CHARACTER"] = ("ВЛАСНИЙ ПЕРСОНАЖ", "СВОЙ ПЕРСОНАЖ"),
        ["Each player plays their own character."] = ("Кожен грає власним персонажем.", "Каждый играет своим персонажем."),
        ["Roleplay connection preview"] = ("Перегляд інструкції Roleplay", "Просмотр инструкции Roleplay"),
        ["JOIN THE SESSION"] = ("ПРИЄДНАЙТЕСЯ ДО СЕСІЇ", "ПРИСОЕДИНИТЕСЬ К СЕССИИ"),
        ["Accept the invite while KOTOR II is running."] = ("Прийміть запрошення, коли KOTOR II запущено.", "Примите приглашение при запущенной KOTOR II."),
        ["Ready when you are. Steam Overlay handles the invite in game."] = ("Усе готово. Запрошуйте друга через оверлей Steam у грі.", "Всё готово. Приглашайте друга через оверлей Steam в игре."),
        ["TWO PLAYERS. ONE GALACTIC JOURNEY."] = ("ДВОЄ ГРАВЦІВ. ОДНА ГАЛАКТИЧНА ПОДОРОЖ.", "ДВА ИГРОКА. ОДНО ГАЛАКТИЧЕСКОЕ ПУТЕШЕСТВИЕ."),
        ["Build {0}  /  Unofficial community mod"] = ("Версія {0}  /  Неофіційний мод", "Версия {0}  /  Неофициальный мод"),
        ["MOD MANAGER"] = ("МЕНЕДЖЕР МОДІВ", "МЕНЕДЖЕР МОДОВ"),
        ["Right-click a mod to enable or disable it. Applies on next launch."] = ("Клацніть мод правою кнопкою. Зміни діють після перезапуску.", "Нажмите на мод правой кнопкой. Изменения действуют после перезапуска."),
        ["ADD NEXUS MOD FOLDER"] = ("ДОДАТИ ПАПКУ МОДУ NEXUS", "ДОБАВИТЬ ПАПКУ МОДА NEXUS"),
        ["Select a separate mod folder containing Override, Modules or Lips."] = ("Виберіть окрему папку моду з Override, Modules або Lips.", "Выберите отдельную папку мода с Override, Modules или Lips."),
        ["Mod"] = ("Мод", "Мод"),
        ["Source"] = ("Джерело", "Источник"),
        ["Status"] = ("Стан", "Статус"),
        ["Enable"] = ("Увімкнути", "Включить"),
        ["Disable"] = ("Вимкнути", "Выключить"),
        ["Active"] = ("Активний", "Активирован"),
        ["Inactive"] = ("Неактивний", "Неактивирован"),
        ["Closed Beta"] = ("Закрита бета", "Закрытая бета"),
        ["COPY MOD LIST"] = ("СКОПІЮВАТИ СПИСОК", "СКОПИРОВАТЬ СПИСОК"),
        ["PASTE MOD LIST"] = ("ВСТАВИТИ СПИСОК", "ВСТАВИТЬ СПИСОК"),
        ["Active mod list copied."] = ("Список активних модів скопійовано.", "Список активных модов скопирован."),
        ["Copy a Force Bond mod list first."] = ("Спочатку скопіюйте список модів Force Bond.", "Сначала скопируйте список модов Force Bond."),
        ["Install or register the missing mods, then paste the list again."] = ("Встановіть або додайте відсутні моди, а потім вставте список ще раз.", "Установите или добавьте недостающие моды, затем вставьте список снова."),
        ["Replace your active mods with this list?"] = ("Замінити ваш список активних модів цим списком?", "Заменить ваш список активных модов этим списком?"),
        ["SETTINGS"] = ("НАЛАШТУВАННЯ", "НАСТРОЙКИ"),
        ["DIAGNOSTICS"] = ("ДІАГНОСТИКА", "ДИАГНОСТИКА"),
        ["LOG"] = ("ЖУРНАЛ", "ЖУРНАЛ"),
        ["VERIFY GAME FILES"] = ("ПЕРЕВІРИТИ ФАЙЛИ", "ВЫПОЛНИТЬ ПРОВЕРКУ ФАЙЛОВ"),
        ["Checks and repairs Force Bond files. Close KOTOR II first."] = ("Перевірка й відновлення файлів Force Bond. Спочатку закрийте KOTOR II.", "Проверка и восстановление файлов Force Bond. Сначала закройте KOTOR II."),
        ["Checking Force Bond files..."] = ("Перевірка файлів Force Bond...", "Проверка файлов Force Bond..."),
        ["File verification completed."] = ("Перевірку файлів завершено.", "Проверка файлов завершена."),
        ["File verification failed."] = ("Не вдалося перевірити файли.", "Не удалось проверить файлы."),
        ["File verification"] = ("Перевірка файлів", "Проверка файлов"),
        ["Close KOTOR II before verifying files."] = ("Закрийте KOTOR II перед перевіркою файлів.", "Закройте KOTOR II перед проверкой файлов."),
        ["Another launcher operation is in progress."] = ("Лаунчер уже виконує іншу операцію.", "Лаунчер уже выполняет другую операцию."),
        ["Files checked: {0}"] = ("Перевірено файлів: {0}", "Проверено файлов: {0}"),
        ["Files restored: {0}"] = ("Відновлено файлів: {0}", "Восстановлено файлов: {0}"),
        ["Old files archived: {0}"] = ("Старих файлів в архіві: {0}", "Старых файлов в архиве: {0}"),
        ["Backup folders relocated: {0}"] = ("Перенесено папок резервних копій: {0}", "Перенесено папок резервных копий: {0}"),
        ["All Force Bond files are up to date."] = ("Усі файли Force Bond актуальні.", "Все файлы Force Bond актуальны."),
        ["Backup folder: {0}"] = ("Папка резервних копій: {0}", "Папка резервных копий: {0}"),
        ["Game language"] = ("Мова гри", "Язык игры"),
        ["Session settings"] = ("Налаштування сесії", "Настройки сессии"),
        ["SESSION SETTINGS"] = ("НАЛАШТУВАННЯ СЕСІЇ", "НАСТРОЙКИ СЕССИИ"),
        ["Language"] = ("Мова", "Язык"),
        ["Text chat"] = ("Текстовий чат", "Текстовый чат"),
        ["Proximity microphone"] = ("Голосовий чат поблизу", "Голосовой чат поблизости"),
        ["Partner voice volume"] = ("Гучність голосу партнера", "Громкость голоса партнёра"),
        ["Show Discord Rich Presence"] = ("Показувати активність у Discord", "Показывать активность в Discord"),
        ["Enable Debug Menu (F7, host only)"] = ("Увімкнути Debug Menu (F7, лише хост)", "Включить Debug Menu (F7, только хост)"),
        ["Borderless Fullscreen (restart required)"] = ("На весь екран без рамки (після перезапуску)", "На весь экран без рамки (после перезапуска)"),
        ["Close KOTOR II and launch it again to apply Borderless Fullscreen."] = ("Закрийте KOTOR II та запустіть знову для режиму без рамки.", "Закройте KOTOR II и запустите заново для режима без рамки."),
        ["F7 is reserved while Debug Menu is enabled."] = ("F7 зайнята, коли Debug Menu увімкнено.", "F7 занята, когда Debug Menu включено."),
        ["Applies while the game is running. Hold the microphone key to talk;\ndouble-tap to lock. F8 and F9 are reserved for Steam."] = ("Зміни діють і під час гри. Утримуйте клавішу мікрофона, щоб говорити;\nподвійне натискання вмикає постійну передачу. F8 та F9 — для Steam.", "Изменения действуют и во время игры. Удерживайте клавишу микрофона;\nдвойное нажатие включает постоянную передачу. F8 и F9 — для Steam."),
        ["SAVE SETTINGS"] = ("ЗБЕРЕГТИ", "СОХРАНИТЬ"),
        ["Could not save settings"] = ("Не вдалося зберегти налаштування", "Не удалось сохранить настройки"),
        ["Choose valid, distinct chat and voice keys."] = ("Виберіть різні допустимі клавіші чату та мікрофона.", "Выберите разные допустимые клавиши чата и микрофона."),
        ["Settings saved. Running sessions apply them automatically."] = ("Налаштування збережено. Активна сесія застосує їх автоматично.", "Настройки сохранены. Активная сессия применит их автоматически."),
        ["Session diagnostics"] = ("Діагностика сесії", "Диагностика сессии"),
        ["SESSION DIAGNOSTICS"] = ("ДІАГНОСТИКА СЕСІЇ", "ДИАГНОСТИКА СЕССИИ"),
        ["Session diagnostic report"] = ("Діагностичний звіт сесії", "Диагностический отчёт сессии"),
        ["COPY REPORT"] = ("КОПІЮВАТИ ЗВІТ", "КОПИРОВАТЬ ОТЧЁТ"),
        ["COPIED"] = ("СКОПІЙОВАНО", "СКОПИРОВАНО"),
        ["OPEN GAME LOG"] = ("ВІДКРИТИ ЖУРНАЛ", "ОТКРЫТЬ ЖУРНАЛ"),
        ["Clipboard unavailable"] = ("Буфер обміну недоступний", "Буфер обмена недоступен"),
        ["No game log yet. Launch Force Bond first."] = ("Журналу гри ще немає. Спершу запустіть Force Bond.", "Журнала игры ещё нет. Сначала запустите Force Bond."),
        ["Could not open the log: {0}"] = ("Не вдалося відкрити журнал: {0}", "Не удалось открыть журнал: {0}"),
        ["Could not open link: {0}"] = ("Не вдалося відкрити посилання: {0}", "Не удалось открыть ссылку: {0}"),
        ["Join Force Bond on Discord"] = ("Приєднатися до Force Bond у Discord", "Присоединиться к Force Bond в Discord"),
        ["Support us on Patreon"] = ("Підтримати нас на Patreon", "Поддержать нас на Patreon"),
        ["Watch Syndicate Corp on YouTube"] = ("Дивитися Syndicate Corp на YouTube", "Смотреть Syndicate Corp на YouTube"),
        ["GAME"] = ("ГРА", "ИГРА"), ["RUNTIME"] = ("МОД", "МОД"), ["WORLD"] = ("СВІТ", "МИР"),
        ["PARTNER"] = ("ПАРТНЕР", "ПАРТНЁР"), ["SYNC"] = ("ЗВ’ЯЗОК", "СВЯЗЬ"),
        ["ready"] = ("готово", "готово"), ["waiting"] = ("очікування", "ожидание"),
        ["Live co-op session progress"] = ("Поточний стан кооперативної сесії", "Текущее состояние кооперативной сессии"),
        ["Launch the game to begin your co-op session."] = ("Запустіть гру, щоб почати кооперативну сесію.", "Запустите игру, чтобы начать кооперативную сессию."),
        ["READY TO LAUNCH"] = ("ГОТОВО ДО ЗАПУСКУ", "ГОТОВО К ЗАПУСКУ"),
        ["Game running. Connect Force Bond to enable co-op."] = ("Гру запущено. Підключіть Force Bond для кооперативу.", "Игра запущена. Подключите Force Bond для кооператива."),
        ["GAME OPEN / MOD NOT LOADED"] = ("ГРУ ЗАПУЩЕНО / МОД НЕ ЗАВАНТАЖЕНО", "ИГРА ЗАПУЩЕНА / МОД НЕ ЗАГРУЖЕН"),
        ["Runtime loaded; live status unavailable. Restart with this build if needed."] = ("Мод завантажено, але стан недоступний. Спробуйте перезапустити гру через цей лаунчер.", "Мод загружен, но состояние недоступно. Попробуйте перезапустить игру через этот лаунчер."),
        ["LIVE STATUS UNAVAILABLE"] = ("ПОТОЧНИЙ СТАН НЕДОСТУПНИЙ", "ТЕКУЩЕЕ СОСТОЯНИЕ НЕДОСТУПНО"),
        ["The in-game overlay could not load. Open Diagnostics."] = ("Не вдалося завантажити оверлей. Відкрийте діагностику.", "Не удалось загрузить оверлей. Откройте диагностику."),
        ["OVERLAY UNAVAILABLE"] = ("ОВЕРЛЕЙ НЕДОСТУПНИЙ", "ОВЕРЛЕЙ НЕДОСТУПЕН"),
        ["Load a save on Peragus - Administration Level."] = ("Завантажте збереження: Перагус — адміністративний рівень.", "Загрузите сохранение: Перагус — административный уровень."),
        ["RUNTIME LOADED / MAIN MENU"] = ("МОД ЗАВАНТАЖЕНО / ГОЛОВНЕ МЕНЮ", "МОД ЗАГРУЖЕН / ГЛАВНОЕ МЕНЮ"),
        ["World loaded; co-op initialization is pending. Open Diagnostics if it persists."] = ("Світ завантажено; очікуємо запуску кооперативу. Якщо стан не змінюється, відкрийте діагностику.", "Мир загружен; ожидаем запуска кооператива. Если состояние не меняется, откройте диагностику."),
        ["INITIALIZING CO-OP WORLD"] = ("ПІДГОТОВКА КООПЕРАТИВНОГО СВІТУ", "ПОДГОТОВКА КООПЕРАТИВНОГО МИРА"),
        ["World ready; waiting for your character's appearance."] = ("Світ готовий; очікуємо даних зовнішності персонажа.", "Мир готов; ожидаем данных внешности персонажа."),
        ["READING CHARACTER PROFILE"] = ("ЗЧИТУВАННЯ ПРОФІЛЮ ПЕРСОНАЖА", "ЧТЕНИЕ ПРОФИЛЯ ПЕРСОНАЖА"),
        ["World ready. Invite or join a friend through Steam Friends."] = ("Світ готовий. Запросіть друга або приєднайтеся через Steam Friends.", "Мир готов. Пригласите друга или присоединитесь через список друзей Steam."),
        ["WORLD READY / WAITING FOR PARTNER"] = ("СВІТ ГОТОВИЙ / ОЧІКУЄМО ПАРТНЕРА", "МИР ГОТОВ / ОЖИДАНИЕ ПАРТНЁРА"),
        ["Partner connected; waiting for the NPC and movement updates."] = ("Партнер підключився; очікуємо його персонажа та оновлень руху.", "Партнёр подключился; ожидаем его персонажа и обновлений движения."),
        ["PARTNER CONNECTED / SYNC PENDING"] = ("ПАРТНЕР У СЕСІЇ / ОЧІКУЄМО ЗВ’ЯЗКУ", "ПАРТНЁР В СЕССИИ / ОЖИДАНИЕ СВЯЗИ"),
        ["Partner NPC present and fresh movement updates received."] = ("Персонаж партнера присутній; оновлення руху надходять.", "Персонаж партнёра присутствует; обновления движения поступают."),
        ["MOVEMENT LINK ACTIVE"] = ("СИНХРОНІЗАЦІЯ РУХУ АКТИВНА", "СИНХРОНИЗАЦИЯ ДВИЖЕНИЯ АКТИВНА"),
        ["Native movement driver is not bound. Open Diagnostics."] = ("Нативний драйвер руху не підключено до NPC. Відкрийте діагностику.", "Нативный драйвер движения не привязан к NPC. Откройте диагностику."),
        ["Checking Steam and game files..."] = ("Перевірка Steam та файлів гри…", "Проверка Steam и файлов игры…"),
        ["Force Bond started. Waiting for the Discord invitation..."] = ("Force Bond запущено. Очікування запрошення Discord…", "Force Bond запущен. Ожидание приглашения Discord…"),
        ["Enable Discord integration in Settings, then accept the invitation again."] = ("Увімкніть інтеграцію Discord у налаштуваннях і прийміть запрошення знову.", "Включите интеграцию Discord в настройках и примите приглашение повторно."),
        ["Start Steam, then launch Force Bond or accept the invitation again."] = ("Запустіть Steam, потім Force Bond або прийміть запрошення знову.", "Запустите Steam, затем Force Bond или примите приглашение повторно."),
        ["Force Bond is already starting. Please wait for the game window."] = ("Force Bond уже запускається. Зачекайте на вікно гри.", "Force Bond уже запускается. Дождитесь окна игры."),
        ["Launching KOTOR II through Steam..."] = ("Запуск KOTOR II через Steam…", "Запуск KOTOR II через Steam…"),
        ["KOTOR II is already running. Connecting Force Bond..."] = ("KOTOR II вже запущено. Підключення Force Bond…", "KOTOR II уже запущена. Подключение Force Bond…"),
        ["Connecting Force Bond to the game..."] = ("Підключення Force Bond до гри…", "Подключение Force Bond к игре…"),
        ["Runtime loaded. Load Peragus - Administration Level and watch the session status."] = ("Мод завантажено. Завантажте адміністративний рівень Перагуса та стежте за станом сесії.", "Мод загружен. Загрузите административный уровень Перагуса и следите за состоянием сессии."),
        ["Runtime loaded. Load Peragus - Administration Level; invite through Steam Friends."] = ("Мод завантажено. Завантажте адміністративний рівень Перагуса; запросіть друга через Steam.", "Мод загружен. Загрузите административный уровень Перагуса; пригласите друга через Steam."),
        ["Launch cancelled."] = ("Запуск скасовано.", "Запуск отменён."),
        ["KOTOR II was not found. Use Change Game Folder to select its installation folder."] = ("KOTOR II не знайдено. Виберіть папку встановлення гри.", "KOTOR II не найдена. Выберите папку установки игры."),
        ["Steam was not found. Install or launch the Steam client first."] = ("Steam не знайдено. Спершу встановіть або запустіть клієнт Steam.", "Steam не найден. Сначала установите или запустите клиент Steam."),
        ["KOTOR II did not start. Launch it through Steam, then try again."] = ("KOTOR II не запустилася. Запустіть її через Steam та спробуйте ще раз.", "KOTOR II не запустилась. Запустите её через Steam и попробуйте снова."),
        ["KOTOR II closed before it was ready."] = ("KOTOR II закрилася до завершення запуску.", "KOTOR II закрылась до завершения запуска."),
        ["KOTOR II did not show a game window. Wait for the main menu and try again."] = ("Вікно KOTOR II не з’явилося. Дочекайтеся головного меню та спробуйте знову.", "Окно KOTOR II не появилось. Дождитесь главного меню и попробуйте снова."),
        ["Force Bond support files are missing. Reinstall the launcher package."] = ("Відсутні файли Force Bond. Розпакуйте повний пакет лаунчера знову.", "Отсутствуют файлы Force Bond. Распакуйте полный пакет лаунчера заново."),
        ["Required Force Bond global catalog is missing."] = ("Відсутній каталог глобальних змінних Force Bond. Розпакуйте повний пакет.", "Отсутствует каталог глобальных переменных Force Bond. Распакуйте полный пакет."),
        ["Cosmetic equipment templates are missing. Extract the complete Force Bond package."] = ("Відсутні шаблони спорядження. Розпакуйте повний пакет Force Bond.", "Отсутствуют шаблоны снаряжения. Распакуйте полный пакет Force Bond."),
        ["Required game resource is missing: {0}"] = ("Відсутній необхідний файл гри: {0}", "Отсутствует необходимый файл игры: {0}"),
        ["Required module hook is missing: 101PER.mod"] = ("Відсутній необхідний файл модуля: 101PER.mod", "Отсутствует необходимый файл модуля: 101PER.mod"),
        ["A different Modules\\101PER.mod is already installed. Force Bond will not overwrite or discard that module mod."] = ("Уже встановлено інший Modules\\101PER.mod. Force Bond не перезаписуватиме цей модуль.", "Уже установлен другой Modules\\101PER.mod. Force Bond не будет перезаписывать этот модуль."),
        ["Close KOTOR II once so Force Bond can install its game resources. Then launch the game from here."] = ("Закрийте KOTOR II, щоб Force Bond встановив свої файли. Потім запустіть гру звідси.", "Закройте KOTOR II, чтобы Force Bond установил свои файлы. Затем запустите игру отсюда."),
        ["Select the Knights of the Old Republic II installation folder (the folder containing swkotor2.exe)."] = ("Виберіть папку Knights of the Old Republic II, що містить swkotor2.exe.", "Выберите папку Knights of the Old Republic II, содержащую swkotor2.exe."),
        ["That folder does not contain swkotor2.exe."] = ("У цій папці немає swkotor2.exe.", "В этой папке нет swkotor2.exe."),
        ["Game folder saved."] = ("Папку гри збережено.", "Папка игры сохранена."),
        ["FORCE BOND / SESSION DIAGNOSTICS"] = ("FORCE BOND / ДІАГНОСТИКА СЕСІЇ", "FORCE BOND / ДИАГНОСТИКА СЕССИИ"),
        ["Launcher build:"] = ("Версія лаунчера:", "Версия лаунчера:"),
        ["Captured UTC:"] = ("Час UTC:", "Время UTC:"),
        ["Game folder:"] = ("Папка гри:", "Папка игры:"), ["Game process:"] = ("Процес гри:", "Процесс игры:"),
        ["Session:"] = ("Сесія:", "Сессия:"), ["Detail:"] = ("Подробиці:", "Подробности:"),
        ["Settings:"] = ("Налаштування:", "Настройки:"), ["Log:"] = ("Журнал:", "Журнал:"),
        ["Not detected"] = ("Не знайдено", "Не найдено"), ["Not running"] = ("Не запущено", "Не запущено"),
        ["No runtime log in this launcher session"] = ("Журналу в цій сесії лаунчера ще немає", "Журнала в этой сессии лаунчера ещё нет"),
        ["LIVE RUNTIME STATUS"] = ("ПОТОЧНИЙ СТАН МОДА", "ТЕКУЩЕЕ СОСТОЯНИЕ МОДА"),
        ["RECENT RUNTIME LOG"] = ("ОСТАННІ ЗАПИСИ ЖУРНАЛУ", "ПОСЛЕДНИЕ ЗАПИСИ ЖУРНАЛА"),
        ["No fresh status for the current game process."] = ("Немає актуального стану поточного процесу гри.", "Нет актуального состояния текущего процесса игры."),
        ["No log available."] = ("Журнал недоступний.", "Журнал недоступен."),
        ["Log could not be read: {0}"] = ("Не вдалося прочитати журнал: {0}", "Не удалось прочитать журнал: {0}"),
    };
}
