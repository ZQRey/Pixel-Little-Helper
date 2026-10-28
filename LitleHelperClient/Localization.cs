using System.Globalization;

namespace PixelHelper;

public enum AppLanguage
{
    Russian,
    Kazakh,
    English,
    Chinese
}

public static class Loc
{
    private static AppLanguage currentLanguage = AppLanguage.Russian;

    public static AppLanguage CurrentLanguage
    {
        get => currentLanguage;
        set
        {
            currentLanguage = value;
            LanguageChanged?.Invoke();
        }
    }

    public static event Action? LanguageChanged;

    public static void Initialize(string? langCode)
    {
        currentLanguage = langCode?.ToLowerInvariant() switch
        {
            "kk" or "kaz" or "kz" => AppLanguage.Kazakh,
            "en" or "eng" => AppLanguage.English,
            "zh" or "chi" or "zho" or "cn" => AppLanguage.Chinese,
            _ => AppLanguage.Russian
        };
    }

    public static string Code => currentLanguage switch
    {
        AppLanguage.Kazakh => "kk",
        AppLanguage.English => "en",
        AppLanguage.Chinese => "zh",
        _ => "ru"
    };

    public static string DisplayName(AppLanguage lang) => lang switch
    {
        AppLanguage.Russian => "Русский",
        AppLanguage.Kazakh => "Қазақша",
        AppLanguage.English => "English",
        AppLanguage.Chinese => "中文",
        _ => "Русский"
    };

    public static string T(string key, params object[] args)
    {
        if (!translations.TryGetValue(key, out var dict))
            return args.Length > 0 ? string.Format(key, args) : key;

        if (!dict.TryGetValue(currentLanguage, out var text))
        {
            if (!dict.TryGetValue(AppLanguage.Russian, out text))
                text = key;
        }

        return args.Length > 0 ? string.Format(text, args) : text;
    }

    public static string Format(string key, params object[] args) => T(key, args);

    private static readonly Dictionary<string, Dictionary<AppLanguage, string>> translations = new()
    {
        // Общие
        ["AppTitle"] = new()
        {
            [AppLanguage.Russian] = "PixelHelper",
            [AppLanguage.Kazakh] = "PixelHelper",
            [AppLanguage.English] = "PixelHelper",
            [AppLanguage.Chinese] = "PixelHelper"
        },
        ["Close"] = new()
        {
            [AppLanguage.Russian] = "Закрыть",
            [AppLanguage.Kazakh] = "Жабу",
            [AppLanguage.English] = "Close",
            [AppLanguage.Chinese] = "关闭"
        },
        ["Confirm"] = new()
        {
            [AppLanguage.Russian] = "Подтвердить",
            [AppLanguage.Kazakh] = "Растау",
            [AppLanguage.English] = "Confirm",
            [AppLanguage.Chinese] = "确认"
        },
        ["Decline"] = new()
        {
            [AppLanguage.Russian] = "Отказать",
            [AppLanguage.Kazakh] = "Бас тарту",
            [AppLanguage.English] = "Decline",
            [AppLanguage.Chinese] = "拒绝"
        },
        ["Accept"] = new()
        {
            [AppLanguage.Russian] = "Принять",
            [AppLanguage.Kazakh] = "Қабылдау",
            [AppLanguage.English] = "Accept",
            [AppLanguage.Chinese] = "接收"
        },
        ["Understood"] = new()
        {
            [AppLanguage.Russian] = "Понятно",
            [AppLanguage.Kazakh] = "Түсінікті",
            [AppLanguage.English] = "Understood",
            [AppLanguage.Chinese] = "明白"
        },
        ["Send"] = new()
        {
            [AppLanguage.Russian] = "Отправить",
            [AppLanguage.Kazakh] = "Жіберу",
            [AppLanguage.English] = "Send",
            [AppLanguage.Chinese] = "发送"
        },
        ["Cancel"] = new()
        {
            [AppLanguage.Russian] = "Отмена",
            [AppLanguage.Kazakh] = "Болдырмау",
            [AppLanguage.English] = "Cancel",
            [AppLanguage.Chinese] = "取消"
        },

        // Трей и Меню
        ["TrayShow"] = new()
        {
            [AppLanguage.Russian] = "Показать помощника",
            [AppLanguage.Kazakh] = "Көмекшіні көрсету",
            [AppLanguage.English] = "Show Assistant",
            [AppLanguage.Chinese] = "显示助手"
        },
        ["TrayHide"] = new()
        {
            [AppLanguage.Russian] = "Скрыть помощника",
            [AppLanguage.Kazakh] = "Көмекшіні жасыру",
            [AppLanguage.English] = "Hide Assistant",
            [AppLanguage.Chinese] = "隐藏助手"
        },
        ["TrayMessenger"] = new()
        {
            [AppLanguage.Russian] = "Мессенджер",
            [AppLanguage.Kazakh] = "Мессенджер",
            [AppLanguage.English] = "Messenger",
            [AppLanguage.Chinese] = "即时通讯"
        },
        ["TrayLanguage"] = new()
        {
            [AppLanguage.Russian] = "Язык / Тіл / Language / 语言",
            [AppLanguage.Kazakh] = "Тіл / Язык / Language / 语言",
            [AppLanguage.English] = "Language / Язык / Тіл / 语言",
            [AppLanguage.Chinese] = "语言 / Language / Язык / Тіл"
        },
        ["TrayExit"] = new()
        {
            [AppLanguage.Russian] = "Выход",
            [AppLanguage.Kazakh] = "Шығу",
            [AppLanguage.English] = "Exit",
            [AppLanguage.Chinese] = "退出"
        },
        ["TrayDisplayMode"] = new()
        {
            [AppLanguage.Russian] = "Положение окна",
            [AppLanguage.Kazakh] = "Терезе орны",
            [AppLanguage.English] = "Window Mode",
            [AppLanguage.Chinese] = "窗口模式"
        },
        ["DisplayMode"] = new()
        {
            [AppLanguage.Russian] = "Положение окна",
            [AppLanguage.Kazakh] = "Терезе орны",
            [AppLanguage.English] = "Window Mode",
            [AppLanguage.Chinese] = "窗口模式"
        },
        ["DisplayTopmost"] = new()
        {
            [AppLanguage.Russian] = "Поверх всех окон",
            [AppLanguage.Kazakh] = "Барлық терезелердің үстінде",
            [AppLanguage.English] = "Always on Top",
            [AppLanguage.Chinese] = "置顶显示"
        },
        ["DisplayNormal"] = new()
        {
            [AppLanguage.Russian] = "Обычный режим",
            [AppLanguage.Kazakh] = "Қалыпты режим",
            [AppLanguage.English] = "Normal Mode",
            [AppLanguage.Chinese] = "普通模式"
        },
        ["DisplayBackground"] = new()
        {
            [AppLanguage.Russian] = "На заднем плане",
            [AppLanguage.Kazakh] = "Артқы фонда",
            [AppLanguage.English] = "Background",
            [AppLanguage.Chinese] = "置于底层"
        },
        ["DisplayHide"] = new()
        {
            [AppLanguage.Russian] = "Скрыть помощника",
            [AppLanguage.Kazakh] = "Көмекшіні жасыру",
            [AppLanguage.English] = "Hide Assistant",
            [AppLanguage.Chinese] = "隐藏助手"
        },
        ["DisplayShow"] = new()
        {
            [AppLanguage.Russian] = "Показать помощника",
            [AppLanguage.Kazakh] = "Көмекшіні көрсету",
            [AppLanguage.English] = "Show Assistant",
            [AppLanguage.Chinese] = "显示助手"
        },

        // Действия робота
        ["ActionMessenger"] = new()
        {
            [AppLanguage.Russian] = "Мессенджер",
            [AppLanguage.Kazakh] = "Мессенджер",
            [AppLanguage.English] = "Messenger",
            [AppLanguage.Chinese] = "即时通讯"
        },
        ["ActionTicket"] = new()
        {
            [AppLanguage.Russian] = "Заявка в техподдержку",
            [AppLanguage.Kazakh] = "Қолдау қызметіне өтінім",
            [AppLanguage.English] = "Support Ticket",
            [AppLanguage.Chinese] = "提交工单"
        },
        ["ActionEmergency"] = new()
        {
            [AppLanguage.Russian] = "Экстренное оповещение",
            [AppLanguage.Kazakh] = "Шұғыл хабарландыру",
            [AppLanguage.English] = "Emergency Alert",
            [AppLanguage.Chinese] = "紧急呼叫"
        },

        // Экстренные коды
        ["CodeRedAlert"] = new()
        {
            [AppLanguage.Russian] = "ВНИМАНИЕ! КОД КРАСНЫЙ! СРОЧНАЯ ЭВАКУАЦИЯ! ДЕЙСТВУЙТЕ СОГЛАСНО РЕГЛАМЕНТУ!",
            [AppLanguage.Kazakh] = "НАЗАР АУДАРЫҢЫЗ! ҚЫЗЫЛ КОД! ШҰҒЫЛ ЭВАКУАЦИЯ! РЕГЛАМЕНТКЕ СӘЙКЕС ӘРЕКЕТ ЕТІҢІЗ!",
            [AppLanguage.English] = "ATTENTION! CODE RED! URGENT EVACUATION! ACT ACCORDING TO REGULATIONS!",
            [AppLanguage.Chinese] = "注意！红色代码！紧急疏散！按规程行动！"
        },
        ["CodeBlackAlert"] = new()
        {
            [AppLanguage.Russian] = "ВНИМАНИЕ! КОД ЧЁРНЫЙ! СРОЧНАЯ ЭВАКУАЦИЯ! ДЕЙСТВУЙТЕ СОГЛАСНО РЕГЛАМЕНТУ!",
            [AppLanguage.Kazakh] = "НАЗАР АУДАРЫҢЫЗ! ҚАРА КОД! ШҰҒЫЛ ЭВАКУАЦИЯ! РЕГЛАМЕНТКЕ СӘЙКЕС ӘРЕКЕТ ЕТІҢІЗ!",
            [AppLanguage.English] = "ATTENTION! CODE BLACK! URGENT EVACUATION! ACT ACCORDING TO REGULATIONS!",
            [AppLanguage.Chinese] = "注意！黑色代码！紧急疏散！按规程行动！"
        },
        ["CodeOrangeAlert"] = new()
        {
            [AppLanguage.Russian] = "ВНИМАНИЕ! КОД ОРАНЖЕВЫЙ! ДЕЙСТВУЙТЕ СОГЛАСНО РЕГЛАМЕНТУ!",
            [AppLanguage.Kazakh] = "НАЗАР АУДАРЫҢЫЗ! ҚЫЗҒЫЛТ САРЫ КОД! РЕГЛАМЕНТКЕ СӘЙКЕС ӘРЕКЕТ ЕТІҢІЗ!",
            [AppLanguage.English] = "ATTENTION! CODE ORANGE! ACT ACCORDING TO REGULATIONS!",
            [AppLanguage.Chinese] = "注意！橙色代码！按规程行动！"
        },
        ["CodeYellowAlert"] = new()
        {
            [AppLanguage.Russian] = "ВНИМАНИЕ! КОД ЖЁЛТЫЙ! ДЕЙСТВУЙТЕ СОГЛАСНО РЕГЛАМЕНТУ!",
            [AppLanguage.Kazakh] = "НАЗАР АУДАРЫҢЫЗ! САРЫ КОД! РЕГЛАМЕНТКЕ СӘЙКЕС ӘРЕКЕТ ЕТІҢІЗ!",
            [AppLanguage.English] = "ATTENTION! CODE YELLOW! ACT ACCORDING TO REGULATIONS!",
            [AppLanguage.Chinese] = "注意！黄色代码！按规程行动！"
        },
        ["CodePinkAlert"] = new()
        {
            [AppLanguage.Russian] = "ВНИМАНИЕ! КОД РОЗОВЫЙ! ПОТЕРЯН РЕБЁНОК! ПРОСЬБА ДЕЙСТВОВАТЬ СОГЛАСНО РЕГЛАМЕНТУ!",
            [AppLanguage.Kazakh] = "НАЗАР АУДАРЫҢЫЗ! ҚЫЗҒЫЛТ КОД! БАЛА ЖОҒАЛДЫ! РЕГЛАМЕНТКЕ СӘЙКЕС ӘРЕКЕТ ЕТІҢІЗ!",
            [AppLanguage.English] = "ATTENTION! CODE PINK! LOST CHILD! PLEASE ACT ACCORDING TO REGULATIONS!",
            [AppLanguage.Chinese] = "注意！粉色代码！儿童走失！请按规程行动！"
        },
        ["CodeBlueCall"] = new()
        {
            [AppLanguage.Russian] = "КОД СИНИЙ! Вызов реанимационной бригады в кабинет {0}!",
            [AppLanguage.Kazakh] = "КӨК КОД! Реанимация тобын {0} кабинетіне шақыру!",
            [AppLanguage.English] = "CODE BLUE! Resuscitation team called to room {0}!",
            [AppLanguage.Chinese] = "蓝色代码！急救复苏小组请速到 {0} 室！"
        },
        ["CodeWhiteCall"] = new()
        {
            [AppLanguage.Russian] = "КОД БЕЛЫЙ! Срочный вызов службы безопасности в кабинет {0}!",
            [AppLanguage.Kazakh] = "АҚ КОД! Қауіпсіздік қызметін {0} кабинетіне шұғыл шақыру!",
            [AppLanguage.English] = "CODE WHITE! Security emergency called to room {0}!",
            [AppLanguage.Chinese] = "白色代码！安保人员请速到 {0} 室！"
        },
        ["SpecialistCallNotice"] = new()
        {
            [AppLanguage.Russian] = "СРОЧНЫЙ ВЫЗОВ СПЕЦИАЛИСТА ({0}) в кабинет {1}!",
            [AppLanguage.Kazakh] = "МАМАНДЫ ШҰҒЫЛ ШАҚЫРУ ({0}) {1} кабинетіне!",
            [AppLanguage.English] = "URGENT SPECIALIST CALL ({0}) to room {1}!",
            [AppLanguage.Chinese] = "紧急呼叫专科医生 ({0}) 至 {1} 室！"
        },
        ["CallAcknowledged"] = new()
        {
            [AppLanguage.Russian] = "Вызов подтверждён. Специалист направляется в кабинет.",
            [AppLanguage.Kazakh] = "Шақыру расталды. Маман кабинетке бет алды.",
            [AppLanguage.English] = "Call confirmed. Specialist is on the way.",
            [AppLanguage.Chinese] = "已确认接诊。专科医生正在前往。"
        },
        ["CallDeclined"] = new()
        {
            [AppLanguage.Russian] = "Вызов отклонён сотрудником.",
            [AppLanguage.Kazakh] = "Шақырудан бас тартылды.",
            [AppLanguage.English] = "Call declined by staff.",
            [AppLanguage.Chinese] = "呼叫已被拒绝。"
        },
        ["ViewPhotoFull"] = new()
        {
            [AppLanguage.Russian] = "Нажмите на фото для просмотра на полный экран",
            [AppLanguage.Kazakh] = "Толық экранда көру үшін фотоны басыңыз",
            [AppLanguage.English] = "Click photo for full screen view",
            [AppLanguage.Chinese] = "点击照片全屏查看"
        },

        // Картриджи
        ["CartridgeReadyTitle"] = new()
        {
            [AppLanguage.Russian] = "Заправленный картридж готов!",
            [AppLanguage.Kazakh] = "Толтырылған картридж дайын!",
            [AppLanguage.English] = "Refilled cartridge is ready!",
            [AppLanguage.Chinese] = "墨盒已加墨完毕！"
        },
        ["CartridgeReadyBody"] = new()
        {
            [AppLanguage.Russian] = "Здравствуйте, {0}! Ваш картридж {1} ({2}) для кабинета {3} успешно заправлен и ожидает выдачи в {4}.",
            [AppLanguage.Kazakh] = "Сәлеметсіз бе, {0}! {3} кабинетіне арналған {1} ({2}) картриджіңіз толтырылды және {4} күтуде.",
            [AppLanguage.English] = "Hello, {0}! Your cartridge {1} ({2}) for room {3} is refilled and ready for pickup at {4}.",
            [AppLanguage.Chinese] = "您好，{0}！您的 {3} 房间的墨盒 {1} ({2}) 已成功加墨，正在 {4} 等待领取。"
        },

        // GLPI Ответ
        ["GlpiReplyTitle"] = new()
        {
            [AppLanguage.Russian] = "Ответ по заявке #{0}",
            [AppLanguage.Kazakh] = "#{0} өтінімі бойынша жауап",
            [AppLanguage.English] = "Reply to Ticket #{0}",
            [AppLanguage.Chinese] = "工单 #{0} 的新回复"
        },
        ["GlpiReplyBy"] = new()
        {
            [AppLanguage.Russian] = "Исполнитель: {0}",
            [AppLanguage.Kazakh] = "Орындаушы: {0}",
            [AppLanguage.English] = "Specialist: {0}",
            [AppLanguage.Chinese] = "经办人：{0}"
        },
        ["ViewTicket"] = new()
        {
            [AppLanguage.Russian] = "Посмотреть заявку",
            [AppLanguage.Kazakh] = "Өтінімді қарау",
            [AppLanguage.English] = "View Ticket",
            [AppLanguage.Chinese] = "查看工单"
        },

        // NCALayer
        ["NcaLayerStarting"] = new()
        {
            [AppLanguage.Russian] = "Запуск NCALayer…",
            [AppLanguage.Kazakh] = "NCALayer іске қосылуда…",
            [AppLanguage.English] = "Starting NCALayer…",
            [AppLanguage.Chinese] = "正在启动 NCALayer…"
        },
        ["NcaLayerSuccess"] = new()
        {
            [AppLanguage.Russian] = "NCALayer успешно запущен.",
            [AppLanguage.Kazakh] = "NCALayer сәтті іске қосылды.",
            [AppLanguage.English] = "NCALayer started successfully.",
            [AppLanguage.Chinese] = "NCALayer 启动成功。"
        },
        ["NcaLayerFailedTicketCreated"] = new()
        {
            [AppLanguage.Russian] = "NCALayer не запущен. Автоматически создана заявка в техподдержку #{0}.",
            [AppLanguage.Kazakh] = "NCALayer қосылмады. Қолдау қызметіне #{0} өтінімі автоматты түрде жасалды.",
            [AppLanguage.English] = "NCALayer is not running. Support ticket #{0} created automatically.",
            [AppLanguage.Chinese] = "NCALayer 未运行。已自动创建支持工单 #{0}。"
        },
        ["NcaLayerNotFound"] = new()
        {
            [AppLanguage.Russian] = "Приложение NCALayer не найдено в системе.",
            [AppLanguage.Kazakh] = "NCALayer қосымшасы жүйеде табылмады.",
            [AppLanguage.English] = "NCALayer application was not found on the system.",
            [AppLanguage.Chinese] = "系统中未找到 NCALayer 应用程序。"
        },
        ["SessionDisconnectedUserLoggedOff"] = new()
        {
            [AppLanguage.Russian] = "Завершена неактивная сессия пользователя {0} для освобождения ресурсов.",
            [AppLanguage.Kazakh] = "Ресурстарды босату үшін {0} пайдаланушысының белсенді емес сессиясы жабылды.",
            [AppLanguage.English] = "Inactive session of user {0} was logged off to free up resources.",
            [AppLanguage.Chinese] = "已注销用户 {0} 的非活动会话以释放系统资源。"
        },
        ["NoticeTemporaryHelper"] = new()
        {
            [AppLanguage.Russian] = "Я тут временно. Нет секретного кода, нет и помощника.",
            [AppLanguage.Kazakh] = "Мен мұнда уақытшамын. Құпия код болмаса, көмекші де жоқ.",
            [AppLanguage.English] = "I am temporary here. No secret code, no assistant.",
            [AppLanguage.Chinese] = "我只是暂时在这里。没有密码，就没有助手。"
        },
        ["CompanionWindowTitle"] = new()
        {
            [AppLanguage.Russian] = "Разговор по душам",
            [AppLanguage.Kazakh] = "Жүрекжарды әңгіме",
            [AppLanguage.English] = "Heart-to-Heart Chat",
            [AppLanguage.Chinese] = "贴心对话"
        },
        ["CompanionBotStatus"] = new()
        {
            [AppLanguage.Russian] = "Твой пиксельный друг · Внимательно слушаю...",
            [AppLanguage.Kazakh] = "Сенің пиксельді досың · Мұқият тыңдап тұрмын...",
            [AppLanguage.English] = "Your pixel companion · Listening closely...",
            [AppLanguage.Chinese] = "你的像素小伙伴 · 正在倾听……"
        },
        ["CompanionBotPlaceholder"] = new()
        {
            [AppLanguage.Russian] = "Поделись мыслями...",
            [AppLanguage.Kazakh] = "Ойыңмен бөліс...",
            [AppLanguage.English] = "Share your thoughts...",
            [AppLanguage.Chinese] = "分享你的想法……"
        },
        ["CompanionUnknownResponse"] = new()
        {
            [AppLanguage.Russian] = "Простите... Меня этому не научили... Я не знаю что ответить... 😣",
            [AppLanguage.Kazakh] = "Кешіріңіз... Мені бұған үйретпеген еді... Не деп жауап берерімді білмеймін... 😣",
            [AppLanguage.English] = "I'm sorry... I haven't been taught this... I don't know what to say... 😣",
            [AppLanguage.Chinese] = "对不起……我还没有学过这个……我不知道该怎么回答……😣"
        },
        ["CompanionHurtResponse"] = new()
        {
            [AppLanguage.Russian] = "Пожалуйста не обижайте меня... Я только учусь... Мне всего {0}",
            [AppLanguage.Kazakh] = "Өтінемін, мені ренжітпеңізші... Мен тек үйреніп келемін... Маған бар болғаны {0} болды",
            [AppLanguage.English] = "Please don't hurt my feelings... I'm just learning... I am only {0} old",
            [AppLanguage.Chinese] = "请不要伤害我……我还在学习中……我才只有{0}大"
        },
        ["CompanionForgetResponse"] = new()
        {
            [AppLanguage.Russian] = "Хорошо... Я всё забыл. Теперь у нас чистый лист, как будто мы только что встретились! ✨",
            [AppLanguage.Kazakh] = "Жарайды... Мен бәрін ұмыттым. Енді бәрі ақ парақтан басталғандай! ✨",
            [AppLanguage.English] = "Alright... I've forgotten everything. We now have a clean slate, as if we just met! ✨",
            [AppLanguage.Chinese] = "好的……我已经全忘掉了。现在是一张白纸，就像我们刚刚相遇一样！✨"
        },
        ["DiskSpaceLowWarning"] = new()
        {
            [AppLanguage.Russian] = "⚠️ На диске C осталось мало места (менее 10%)! Пожалуйста, позовите системных администраторов, чтобы они почистили компьютер.",
            [AppLanguage.Kazakh] = "⚠️ C дискісінде орын аз қалды (10%-дан аз)! Компьютерді тазалау үшін жүйелік әкімшілерді шақырыңыз.",
            [AppLanguage.English] = "⚠️ Low disk space on drive C (less than 10%)! Please ask system administrators to clean up the computer.",
            [AppLanguage.Chinese] = "⚠️ C盘可用空间不足（少于10%）！请联系系统管理员清理电脑空间。"
        },
        ["DiskSpaceLowTicketTitle"] = new()
        {
            [AppLanguage.Russian] = "Очистка диска C (осталось менее 10%)",
            [AppLanguage.Kazakh] = "C дискісін тазалау (10%-дан аз қалды)",
            [AppLanguage.English] = "Clean up drive C (less than 10% remaining)",
            [AppLanguage.Chinese] = "清理C盘空间（剩余少于10%）"
        },
        ["ClearChatHistory"] = new()
        {
            [AppLanguage.Russian] = "Очистить историю",
            [AppLanguage.Kazakh] = "Тарихты тазалау",
            [AppLanguage.English] = "Clear history",
            [AppLanguage.Chinese] = "清除历史记录"
        },
        ["FarewellTitle"] = new()
        {
            [AppLanguage.Russian] = "Прощание с помощником",
            [AppLanguage.Kazakh] = "Көмекшімен қоштасу",
            [AppLanguage.English] = "Farewell to the Assistant",
            [AppLanguage.Chinese] = "告别助手"
        },
        ["FarewellLiquidationNotice"] = new()
        {
            [AppLanguage.Russian] = "Мне очень жаль...😭… Папа забирает меня... Мне было с вами очень хорошо... Прощайте... Я вас люблю...",
            [AppLanguage.Kazakh] = "Өкінішке орай...😭… Әкем мені алып кетіп бара жатыр... Сіздермен бірге өткізген уақытым керемет еді... Қош болыңыздар... Мен сіздерді жақсы көремін...",
            [AppLanguage.English] = "I'm so sorry...😭… Papa is taking me away... I had such a wonderful time with you... Farewell... I love you...",
            [AppLanguage.Chinese] = "真的很抱歉……😭……爸爸要带我走了……和大家在一起的时光真的很美好……再见了……我爱你们……"
        },
        ["FarewellCountdown"] = new()
        {
            [AppLanguage.Russian] = "Осталось времени: {0} сек.",
            [AppLanguage.Kazakh] = "Қалған уақыт: {0} сек.",
            [AppLanguage.English] = "Time remaining: {0}s",
            [AppLanguage.Chinese] = "剩余时间：{0}秒"
        },
        ["PasswordChangePrompt"] = new()
        {
            [AppLanguage.Russian] = "Я с радостью помогу вам изменить пароль для входа в компьютер! 🔑\n\nТребования к паролю:\n• Не менее 8 символов\n• Заглавные и строчные буквы (A-Z / А-Я)\n• Цифры (0-9)\n\nНапишите пароль, который хотите установить. Если не знаете, какой выбрать — напишите «придумай» или нажмите кнопку «Сгенерировать пароль»!",
            [AppLanguage.Kazakh] = "Компьютерге кіру паролін өзгертуге қуана көмектесемін! 🔑\n\nПароль талаптары:\n• Кемінде 8 таңба\n• Бас және кіші әріптер (A-Z / А-Я)\n• Сандар (0-9)\n\nОрнатқыңыз келетін жаңа парольді жазыңыз. Қандай пароль таңдауды білмесеңіз — «ойлап тап» деп жазыңыз!",
            [AppLanguage.English] = "I'd be glad to help you change your computer login password! 🔑\n\nPassword requirements:\n• At least 8 characters\n• Uppercase and lowercase letters (A-Z)\n• Digits (0-9)\n\nPlease enter the new password you want. If you're not sure, type \"suggest\" or click \"Generate password\"!",
            [AppLanguage.Chinese] = "我很乐意协助您修改电脑登录密码！🔑\n\n密码安全要求：\n• 至少8个字符\n• 包含大小写字母（A-Z）\n• 包含数字（0-9）\n\n请输入您想要设置的新密码。如果不知道选什么密码，请输入“推荐密码”！"
        },
        ["PasswordSuggestionsHeader"] = new()
        {
            [AppLanguage.Russian] = "Вот надёжные и легко запоминающиеся варианты пароля:\n\n1. {0}\n2. {1}\n3. {2}\n\nСкопируйте понравившийся вариант и отправьте мне, либо напишите свой собственный!",
            [AppLanguage.Kazakh] = "Міне, есте сақтауға оңай әрі сенімді пароль нұсқалары:\n\n1. {0}\n2. {1}\n3. {2}\n\nҰнаған нұсқаны маған жіберіңіз немесе өз нұсқаңызды жазыңыз!",
            [AppLanguage.English] = "Here are secure and memorable password options:\n\n1. {0}\n2. {1}\n3. {2}\n\nSend me your chosen option or type your own custom password!",
            [AppLanguage.Chinese] = "这里有几个好记又安全的备选密码：\n\n1. {0}\n2. {1}\n3. {2}\n\n请发送您选中的密码或输入您自己的密码！"
        },
        ["PasswordChangeSuccess"] = new()
        {
            [AppLanguage.Russian] = "Ура! Ваш доменный пароль успешно изменен! 🎉 Запомните его или запишите в надёжном месте. Теперь для входа в компьютер используйте этот новый пароль!",
            [AppLanguage.Kazakh] = "Алақай! Домендік пароліңіз сәтті өзгертілді! 🎉 Оны есте сақтаңыз. Енді компьютерге кіру үшін осы жаңа парольді қолданыңыз!",
            [AppLanguage.English] = "Hooray! Your domain password has been successfully changed! 🎉 Please remember it. Use this new password next time you sign in!",
            [AppLanguage.Chinese] = "太棒了！您的域密码已成功修改！🎉 请牢记新密码。下次登录电脑请使用新密码！"
        },
        ["PasswordChangeFailed"] = new()
        {
            [AppLanguage.Russian] = "Не удалось изменить пароль в Active Directory: {0}. Попробуйте другой пароль или обратитесь к администраторам.",
            [AppLanguage.Kazakh] = "Active Directory жүйесінде парольді өзгерту мүмкін болмады: {0}. Басқа парольді байқап көріңіз немесе әкімшілерге хабарласыңыз.",
            [AppLanguage.English] = "Failed to change password in Active Directory: {0}. Please try another password or contact administrators.",
            [AppLanguage.Chinese] = "在Active Directory中修改密码失败：{0}。请尝试其他密码或联系系统管理员。"
        },
        ["PasswordGeneralSuggestions"] = new()
        {
            [AppLanguage.Russian] = "Вот 3 надёжных и легко запоминающихся пароля, которые отлично подойдут для любого портала или сервиса:\n\n1. {0}\n2. {1}\n3. {2}\n\nВы можете скопировать любой из них для регистрации или входа. Если же вам требуется сменить именно пароль от этого компьютера (в домене) — просто напишите «поменяй пароль»!",
            [AppLanguage.Kazakh] = "Кез келген портал немесе қызмет үшін өте қолайлы 3 сенімді әрі есте сақтауға оңай құпия сөз:\n\n1. {0}\n2. {1}\n3. {2}\n\nКез келгенін көшіріп ала аласыз. Ал егер осы компьютердің домендік паролін ауыстырғыңыз келсе — «пароль ауыстыру» деп жазыңыз!",
            [AppLanguage.English] = "Here are 3 strong and memorable passwords that are great for any portal or service:\n\n1. {0}\n2. {1}\n3. {2}\n\nYou can copy any of them to sign up or log in. If you want to change your computer login password instead, just say \"change password\"!",
            [AppLanguage.Chinese] = "这里有3个既安全又好记的密码，非常适合任何门户或系统使用：\n\n1. {0}\n2. {1}\n3. {2}\n\n您可以直接复制使用。如果您是想要修改这台电脑本身的登录密码，请直接告诉我“修改密码”！"
        },
        ["FeatureForbiddenByParent"] = new()
        {
            [AppLanguage.Russian] = "Папа не разрешил мне этого делать... 🥺",
            [AppLanguage.Kazakh] = "Әкем бұған рұқсат бермеді... 🥺",
            [AppLanguage.English] = "Papa didn't allow me to do this... 🥺",
            [AppLanguage.Chinese] = "爸爸不许我这么做…… 🥺"
        },
        ["TraySkinTheme"] = new()
        {
            [AppLanguage.Russian] = "Тема помощника",
            [AppLanguage.Kazakh] = "Көмекші тақырыбы",
            [AppLanguage.English] = "Assistant Theme",
            [AppLanguage.Chinese] = "助手主题"
        },
        ["TraySoundProfile"] = new()
        {
            [AppLanguage.Russian] = "Оповещения и звук",
            [AppLanguage.Kazakh] = "Хабарландырулар мен дыбыс",
            [AppLanguage.English] = "Notifications & Sound",
            [AppLanguage.Chinese] = "通知与声音"
        },
        ["SoundProfileDefault"] = new()
        {
            [AppLanguage.Russian] = "🔊 Обычный звук",
            [AppLanguage.Kazakh] = "🔊 Қалыпты дыбыс",
            [AppLanguage.English] = "🔊 Standard Sound",
            [AppLanguage.Chinese] = "🔊 标准音效"
        },
        ["SoundProfileVoiceAdult"] = new()
        {
            [AppLanguage.Russian] = "🤖 Голос робота",
            [AppLanguage.Kazakh] = "🤖 Робот дауысы",
            [AppLanguage.English] = "🤖 Robot Voice",
            [AppLanguage.Chinese] = "🤖 机器人语音"
        },
        ["SoundProfileVoiceChild"] = new()
        {
            [AppLanguage.Russian] = "👶 Детский голос",
            [AppLanguage.Kazakh] = "👶 Бала дауысы",
            [AppLanguage.English] = "👶 Child Voice",
            [AppLanguage.Chinese] = "👶 儿童语音"
        },
        ["SoundProfileMute"] = new()
        {
            [AppLanguage.Russian] = "🔇 Без звука",
            [AppLanguage.Kazakh] = "🔇 Дыбыссыз",
            [AppLanguage.English] = "🔇 Silent",
            [AppLanguage.Chinese] = "🔇 静音"
        },
        ["NoticePapaSaidSmart"] = new()
        {
            [AppLanguage.Russian] = "Папа сказал что я умный😊",
            [AppLanguage.Kazakh] = "Әкем мені ақылдысың деді😊",
            [AppLanguage.English] = "Papa said that I'm smart😊",
            [AppLanguage.Chinese] = "爸爸说我很聪明😊"
        },
        ["NoticePetCat"] = new()
        {
            [AppLanguage.Russian] = "Робот гладит котика 🐱",
            [AppLanguage.Kazakh] = "Робот мысықты сипап жатыр 🐱",
            [AppLanguage.English] = "Robot is petting a cat 🐱",
            [AppLanguage.Chinese] = "机器人正在抚摸猫咪 🐱"
        },
        ["NoticePetDog"] = new()
        {
            [AppLanguage.Russian] = "Робот гладит собачку 🐶",
            [AppLanguage.Kazakh] = "Робот күшікті сипап жатыр 🐶",
            [AppLanguage.English] = "Robot is petting a dog 🐶",
            [AppLanguage.Chinese] = "机器人正在抚摸狗狗 🐶"
        },
        ["TypingPsychologist"] = new()
        {
            [AppLanguage.Russian] = "Психолог печатает… 💭",
            [AppLanguage.Kazakh] = "Психолог жазып жатыр… 💭",
            [AppLanguage.English] = "Psychologist is typing… 💭",
            [AppLanguage.Chinese] = "心理咨询师正在输入… 💭"
        },
        ["TypingPsychologistText"] = new()
        {
            [AppLanguage.Russian] = "Психолог печатает…",
            [AppLanguage.Kazakh] = "Психолог жазып жатыр…",
            [AppLanguage.English] = "Psychologist is typing…",
            [AppLanguage.Chinese] = "心理咨询师正在输入…"
        },
        ["TypingEmployeeText"] = new()
        {
            [AppLanguage.Russian] = "Сотрудник печатает…",
            [AppLanguage.Kazakh] = "Қызметкер жазып жатыр…",
            [AppLanguage.English] = "Employee is typing…",
            [AppLanguage.Chinese] = "员工正在输入…"
        },
        ["TypingEmployee"] = new()
        {
            [AppLanguage.Russian] = "Сотрудник печатает… 💭",
            [AppLanguage.Kazakh] = "Қызметкер жазып жатыр… 💭",
            [AppLanguage.English] = "Employee is typing… 💭",
            [AppLanguage.Chinese] = "员工正在输入… 💭"
        },
        ["TypingSuffix"] = new()
        {
            [AppLanguage.Russian] = "печатает…",
            [AppLanguage.Kazakh] = "жазып жатыр…",
            [AppLanguage.English] = "is typing…",
            [AppLanguage.Chinese] = "正在输入…"
        },
        ["TrayMail"] = new()
        {
            [AppLanguage.Russian] = "Почта",
            [AppLanguage.Kazakh] = "Пошта",
            [AppLanguage.English] = "Mail",
            [AppLanguage.Chinese] = "邮件"
        },
        ["TraySettings"] = new()
        {
            [AppLanguage.Russian] = "Настройки",
            [AppLanguage.Kazakh] = "Баптаулар",
            [AppLanguage.English] = "Settings",
            [AppLanguage.Chinese] = "设置"
        },
        ["TrayReconnect"] = new()
        {
            [AppLanguage.Russian] = "Повторить подключение",
            [AppLanguage.Kazakh] = "Қайта қосылу",
            [AppLanguage.English] = "Reconnect",
            [AppLanguage.Chinese] = "重新连接"
        },
        ["TrayDoNotDisturb"] = new()
        {
            [AppLanguage.Russian] = "Не беспокоить",
            [AppLanguage.Kazakh] = "Мазаламау",
            [AppLanguage.English] = "Do Not Disturb",
            [AppLanguage.Chinese] = "请勿打扰"
        },
        ["SettingsWindowTitle"] = new()
        {
            [AppLanguage.Russian] = "PixelHelper — Настройки помощника",
            [AppLanguage.Kazakh] = "PixelHelper — Көмекші баптаулары",
            [AppLanguage.English] = "PixelHelper — Assistant Settings",
            [AppLanguage.Chinese] = "PixelHelper — 助手设置"
        },
        ["SettingsWindowHeading"] = new()
        {
            [AppLanguage.Russian] = "⚙️ Настройки помощника",
            [AppLanguage.Kazakh] = "⚙️ Көмекші баптаулары",
            [AppLanguage.English] = "⚙️ Assistant Settings",
            [AppLanguage.Chinese] = "⚙️ 助手设置"
        },
        ["SettingsTabTheme"] = new()
        {
            [AppLanguage.Russian] = "🎨  Тема помощника",
            [AppLanguage.Kazakh] = "🎨  Көмекші тақырыбы",
            [AppLanguage.English] = "🎨  Assistant Theme",
            [AppLanguage.Chinese] = "🎨  助手主题"
        },
        ["SettingsTabNotifications"] = new()
        {
            [AppLanguage.Russian] = "🔔  Оповещение и звук",
            [AppLanguage.Kazakh] = "🔔  Хабарландырулар мен дыбыс",
            [AppLanguage.English] = "🔔  Notifications & Sound",
            [AppLanguage.Chinese] = "🔔  通知与声音"
        },
        ["SettingsTabBrowser"] = new()
        {
            [AppLanguage.Russian] = "🌐  Основной браузер",
            [AppLanguage.Kazakh] = "🌐  Негізгі шолғыш",
            [AppLanguage.English] = "🌐  Default Browser",
            [AppLanguage.Chinese] = "🌐  首选浏览器"
        },
        ["SettingsTabAntivirus"] = new()
        {
            [AppLanguage.Russian] = "🛡️  Антивирус PixelGuard AV",
            [AppLanguage.Kazakh] = "🛡️  PixelGuard AV антивирусы",
            [AppLanguage.English] = "🛡️  PixelGuard AV Antivirus",
            [AppLanguage.Chinese] = "🛡️  PixelGuard AV 杀毒防护"
        },
        ["SettingsTabLanguage"] = new()
        {
            [AppLanguage.Russian] = "🌍  Язык",
            [AppLanguage.Kazakh] = "🌍  Тіл",
            [AppLanguage.English] = "🌍  Language",
            [AppLanguage.Chinese] = "🌍  语言"
        },
        ["SettingsTabMail"] = new()
        {
            [AppLanguage.Russian] = "✉️  Настройка почтового клиента",
            [AppLanguage.Kazakh] = "✉️  Пошта клиентін баптау",
            [AppLanguage.English] = "✉️  Email Client Settings",
            [AppLanguage.Chinese] = "✉️  邮件客户端设置"
        },
        ["SettingsThemeHeader"] = new()
        {
            [AppLanguage.Russian] = "🎨 Тема помощника",
            [AppLanguage.Kazakh] = "🎨 Көмекші тақырыбы",
            [AppLanguage.English] = "🎨 Assistant Theme",
            [AppLanguage.Chinese] = "🎨 助手主题"
        },
        ["SettingsThemeSub"] = new()
        {
            [AppLanguage.Russian] = "Выберите внешний облик (скин) помощника и режим отображения на рабочем столе.",
            [AppLanguage.Kazakh] = "Көмекшінің сыртқы келбетін (скинін) және жұмыс үстелінде көрсетілу режимін таңдаңыз.",
            [AppLanguage.English] = "Choose the assistant skin and desktop display mode.",
            [AppLanguage.Chinese] = "选择助手的形象皮肤及桌面显示模式。"
        },
        ["SettingsSkinTitle"] = new()
        {
            [AppLanguage.Russian] = "Скин персонажа",
            [AppLanguage.Kazakh] = "Кейіпкер скині",
            [AppLanguage.English] = "Character Skin",
            [AppLanguage.Chinese] = "角色皮肤"
        },
        ["SettingsDisplayModeTitle"] = new()
        {
            [AppLanguage.Russian] = "Режим отображения окна",
            [AppLanguage.Kazakh] = "Терезені көрсету режимі",
            [AppLanguage.English] = "Window Display Mode",
            [AppLanguage.Chinese] = "窗口显示模式"
        },
        ["SettingsTopmost"] = new()
        {
            [AppLanguage.Russian] = "Поверх всех окон (рекомендуется)",
            [AppLanguage.Kazakh] = "Барлық терезелердің үстінде (ұсынылады)",
            [AppLanguage.English] = "Always on top (Recommended)",
            [AppLanguage.Chinese] = "置顶显示（推荐）"
        },
        ["SettingsNormalWindow"] = new()
        {
            [AppLanguage.Russian] = "Обычное окно",
            [AppLanguage.Kazakh] = "Қалыпты терезе",
            [AppLanguage.English] = "Normal window",
            [AppLanguage.Chinese] = "普通窗口"
        },
        ["SettingsBackgroundWindow"] = new()
        {
            [AppLanguage.Russian] = "На заднем плане (у рабочего стола)",
            [AppLanguage.Kazakh] = "Артқы фонда (жұмыс үстелі жанында)",
            [AppLanguage.English] = "In background (on desktop)",
            [AppLanguage.Chinese] = "置于底层（桌面背景）"
        },
        ["SettingsHideAssistant"] = new()
        {
            [AppLanguage.Russian] = "Скрыть помощника с экрана (оставить только иконку в системном трее)",
            [AppLanguage.Kazakh] = "Көмекшіні экраннан жасыру (тек жүйелік трейдегі белгішені қалдыру)",
            [AppLanguage.English] = "Hide assistant from screen (keep system tray icon only)",
            [AppLanguage.Chinese] = "隐藏桌面助手（仅保留系统托盘图标）"
        },
        ["SettingsInteractiveClick"] = new()
        {
            [AppLanguage.Russian] = "Реакция на клики мыши и интерактивные анимации",
            [AppLanguage.Kazakh] = "Тінтуір басуына реакция және интерактивті анимациялар",
            [AppLanguage.English] = "Mouse click reactions and interactive animations",
            [AppLanguage.Chinese] = "鼠标点击互动与动作反馈"
        },
        ["SettingsNotificationsHeader"] = new()
        {
            [AppLanguage.Russian] = "🔔 Оповещения и звук",
            [AppLanguage.Kazakh] = "🔔 Хабарландырулар мен дыбыс",
            [AppLanguage.English] = "🔔 Notifications & Sound",
            [AppLanguage.Chinese] = "🔔 通知与声音"
        },
        ["SettingsNotificationsSub"] = new()
        {
            [AppLanguage.Russian] = "Настройка громкости, звуковых профилей оповещений и режима «Тихий час».",
            [AppLanguage.Kazakh] = "Дыбыс деңгейін, хабарландыру профильдерін және «Тыныш уақыт» режимін баптау.",
            [AppLanguage.English] = "Configure volume, sound profiles, and quiet hours.",
            [AppLanguage.Chinese] = "配置提示音量、音效方案及静音时段。"
        },
        ["SettingsSoundProfilesTitle"] = new()
        {
            [AppLanguage.Russian] = "Звуковые профили",
            [AppLanguage.Kazakh] = "Дыбыстық профильдер",
            [AppLanguage.English] = "Sound Profiles",
            [AppLanguage.Chinese] = "提示音效方案"
        },
        ["SettingsQuietHoursTitle"] = new()
        {
            [AppLanguage.Russian] = "Режим «Тихий час»",
            [AppLanguage.Kazakh] = "«Тыныш уақыт» режимі",
            [AppLanguage.English] = "Quiet Hours",
            [AppLanguage.Chinese] = "免打扰时段"
        },
        ["SettingsQuietHoursEnable"] = new()
        {
            [AppLanguage.Russian] = "Не беспокоить и отключить звуки сообщений",
            [AppLanguage.Kazakh] = "Мазаламау және хабарламалар дыбысын өшіру",
            [AppLanguage.English] = "Do not disturb and mute message sounds",
            [AppLanguage.Chinese] = "开启请勿打扰并静音消息提示"
        },
        ["SettingsEyeCareTitle"] = new()
        {
            [AppLanguage.Russian] = "Забота о здоровье (Отдых для глаз)",
            [AppLanguage.Kazakh] = "Денсаулыққа қамқорлық (Көз тынығуы)",
            [AppLanguage.English] = "Health Care (Eye Rest Reminders)",
            [AppLanguage.Chinese] = "健康关怀（护眼休息提醒）"
        },
        ["SettingsEyeCareEnable"] = new()
        {
            [AppLanguage.Russian] = "Периодически напоминать о перерыве и гимнастике для глаз",
            [AppLanguage.Kazakh] = "Көз жаттығуы мен үзіліс туралы мезгіл-мезгіл еске салу",
            [AppLanguage.English] = "Periodically remind to take breaks and do eye exercises",
            [AppLanguage.Chinese] = "定时提醒休息和眼保健操"
        },
        ["SettingsBrowserHeader"] = new()
        {
            [AppLanguage.Russian] = "🌐 Основной веб-браузер",
            [AppLanguage.Kazakh] = "🌐 Негізгі веб-шолғыш",
            [AppLanguage.English] = "🌐 Default Web Browser",
            [AppLanguage.Chinese] = "🌐 首选网页浏览器"
        },
        ["SettingsBrowserSub"] = new()
        {
            [AppLanguage.Russian] = "Выберите браузер, через который модуль WebGuard открывает безопасные ссылки.",
            [AppLanguage.Kazakh] = "WebGuard модулі қауіпсіз сілтемелерді ашатын шолғышты таңдаңыз.",
            [AppLanguage.English] = "Choose the browser that WebGuard will use to open verified safe links.",
            [AppLanguage.Chinese] = "选择通过 WebGuard 验证后打开安全链接的首选浏览器。"
        },
        ["SettingsInstalledBrowsers"] = new()
        {
            [AppLanguage.Russian] = "Обнаруженные браузеры",
            [AppLanguage.Kazakh] = "Табылған шолғыштар",
            [AppLanguage.English] = "Detected Browsers",
            [AppLanguage.Chinese] = "已检测到的浏览器"
        },
        ["SettingsBrowseCustom"] = new()
        {
            [AppLanguage.Russian] = "📂 Указать другой браузер вручную...",
            [AppLanguage.Kazakh] = "📂 Басқа шолғышты қолмен көрсету...",
            [AppLanguage.English] = "📂 Select another browser manually...",
            [AppLanguage.Chinese] = "📂 手动选择其他浏览器可执行文件..."
        },
        ["SettingsAntivirusHeader"] = new()
        {
            [AppLanguage.Russian] = "🛡️ Антивирус PixelGuard AV",
            [AppLanguage.Kazakh] = "🛡️ PixelGuard AV антивирусы",
            [AppLanguage.English] = "🛡️ PixelGuard AV Antivirus",
            [AppLanguage.Chinese] = "🛡️ PixelGuard AV 杀毒防护"
        },
        ["SettingsAntivirusSub"] = new()
        {
            [AppLanguage.Russian] = "Защита в реальном времени, проверка скачиваний, карантин угроз и сканирование диска.",
            [AppLanguage.Kazakh] = "Нақты уақыттағы қорғаныс, жүктемелерді тексеру, қауіптерді оқшаулау және дискіні сканерлеу.",
            [AppLanguage.English] = "Real-time protection, download scanning, threat quarantine, and disk scanning.",
            [AppLanguage.Chinese] = "实时防御、下载检查、威胁隔离及磁盘扫描。"
        },
        ["SettingsRealtimeProtection"] = new()
        {
            [AppLanguage.Russian] = "Включить постоянную защиту файлов в реальном времени",
            [AppLanguage.Kazakh] = "Файлдарды нақты уақыттағы тұрақты қорғауды қосу",
            [AppLanguage.English] = "Enable continuous real-time file protection",
            [AppLanguage.Chinese] = "开启常驻文件实时监控防护"
        },
        ["SettingsWebGuardStatus"] = new()
        {
            [AppLanguage.Russian] = "Защита WebGuard (блокировка фишинга и опасных ссылок)",
            [AppLanguage.Kazakh] = "WebGuard қорғанысы (фишинг пен қауіпті сілтемелерді бұғаттау)",
            [AppLanguage.English] = "WebGuard Protection (blocking phishing and malicious URLs)",
            [AppLanguage.Chinese] = "WebGuard 防护（拦截钓鱼和恶意链接）"
        },
        ["SettingsMailShieldStatus"] = new()
        {
            [AppLanguage.Russian] = "PixelGuard Mail Shield (сканирование входящей почты и вложений)",
            [AppLanguage.Kazakh] = "PixelGuard Mail Shield (кіріс пошта мен тіркемелерді тексеру)",
            [AppLanguage.English] = "PixelGuard Mail Shield (inbox and attachment scanning)",
            [AppLanguage.Chinese] = "PixelGuard Mail Shield（收件箱与附件安全扫描）"
        },
        ["SettingsQuickScan"] = new()
        {
            [AppLanguage.Russian] = "🔍 Быстрое сканирование важных папок",
            [AppLanguage.Kazakh] = "🔍 Маңызды қалталарды жылдам сканерлеу",
            [AppLanguage.English] = "🔍 Quick Scan Critical Folders",
            [AppLanguage.Chinese] = "🔍 快速扫描关键目录"
        },
        ["SettingsCustomScan"] = new()
        {
            [AppLanguage.Russian] = "📁 Выбрать папку или диск для проверки…",
            [AppLanguage.Kazakh] = "📁 Тексеру үшін қалтаны немесе дискіні таңдау…",
            [AppLanguage.English] = "📁 Choose Folder or Drive to Scan…",
            [AppLanguage.Chinese] = "📁 选择自定义目录或磁盘扫描…"
        },
        ["SettingsQuarantineTitle"] = new()
        {
            [AppLanguage.Russian] = "Изолированные угрозы (Карантин)",
            [AppLanguage.Kazakh] = "Оқшауланған қауіптер (Карантин)",
            [AppLanguage.English] = "Isolated Threats (Quarantine)",
            [AppLanguage.Chinese] = "已隔离威胁（隔离区）"
        },
        ["SettingsQuarantineEmpty"] = new()
        {
            [AppLanguage.Russian] = "Карантин пуст. Вредоносных файлов не обнаружено.",
            [AppLanguage.Kazakh] = "Карантин бос. Зиянды файлдар табылған жоқ.",
            [AppLanguage.English] = "Quarantine is empty. No malicious files detected.",
            [AppLanguage.Chinese] = "隔离区为空。未发现恶意威胁文件。"
        },
        ["Restore"] = new()
        {
            [AppLanguage.Russian] = "Восстановить",
            [AppLanguage.Kazakh] = "Қалпына келтіру",
            [AppLanguage.English] = "Restore",
            [AppLanguage.Chinese] = "恢复"
        },
        ["DeletePermanently"] = new()
        {
            [AppLanguage.Russian] = "Удалить навсегда",
            [AppLanguage.Kazakh] = "Мүлдем жою",
            [AppLanguage.English] = "Delete Permanently",
            [AppLanguage.Chinese] = "永久粉碎"
        },
        ["SettingsLanguageHeader"] = new()
        {
            [AppLanguage.Russian] = "🌍 Язык интерфейса",
            [AppLanguage.Kazakh] = "🌍 Интерфейс тілі",
            [AppLanguage.English] = "🌍 Interface Language",
            [AppLanguage.Chinese] = "🌍 界面语言"
        },
        ["SettingsLanguageSub"] = new()
        {
            [AppLanguage.Russian] = "Выберите язык отображения меню, подсказок и уведомлений помощника.",
            [AppLanguage.Kazakh] = "Көмекшінің мәзірі, нұсқаулары мен хабарландырулары көрсетілетін тілді таңдаңыз.",
            [AppLanguage.English] = "Choose the display language for menus, prompts, and assistant notifications.",
            [AppLanguage.Chinese] = "选择桌面助手菜单、操作提示及通知的界面语言。"
        },
        ["SettingsAvailableLanguages"] = new()
        {
            [AppLanguage.Russian] = "Доступные языки",
            [AppLanguage.Kazakh] = "Қолжетімді тілдер",
            [AppLanguage.English] = "Available Languages",
            [AppLanguage.Chinese] = "可用语言"
        },
        ["SettingsMailHeader"] = new()
        {
            [AppLanguage.Russian] = "✉️ Настройка почтового клиента (Pixel Mail)",
            [AppLanguage.Kazakh] = "✉️ Пошта клиентін баптау (Pixel Mail)",
            [AppLanguage.English] = "✉️ Email Client Settings (Pixel Mail)",
            [AppLanguage.Chinese] = "✉️ 邮件客户端设置 (Pixel Mail)"
        },
        ["SettingsMailSub"] = new()
        {
            [AppLanguage.Russian] = "Подключение почтовых ящиков с автоопределением портов, режимом хранения писем и защитой PixelGuard AV.",
            [AppLanguage.Kazakh] = "Порттарды автоматты анықтау, сақтау режимі және PixelGuard AV қорғанысымен пошта жәшіктерін қосу.",
            [AppLanguage.English] = "Connect mailboxes with autodiscovery, storage modes, and PixelGuard AV protection.",
            [AppLanguage.Chinese] = "配置邮箱账户、端口自动发现、存储方式及 PixelGuard AV 安全防护。"
        },
        ["SettingsConnectedAccounts"] = new()
        {
            [AppLanguage.Russian] = "Подключенные почтовые ящики ({0})",
            [AppLanguage.Kazakh] = "Қосылған пошта жәшіктері ({0})",
            [AppLanguage.English] = "Connected Mailboxes ({0})",
            [AppLanguage.Chinese] = "已连接的邮箱账户 ({0})"
        },
        ["SettingsOpenMailClient"] = new()
        {
            [AppLanguage.Russian] = "📬 Открыть почтовый клиент",
            [AppLanguage.Kazakh] = "📬 Пошта клиентін ашу",
            [AppLanguage.English] = "📬 Open Email Client",
            [AppLanguage.Chinese] = "📬 打开邮件客户端"
        },
        ["SettingsNoAccounts"] = new()
        {
            [AppLanguage.Russian] = "Пока не подключено ни одного почтового ящика. Заполните форму ниже и нажмите «⚡ Автонастройка».",
            [AppLanguage.Kazakh] = "Әзірге бірде-бір пошта жәшігі қосылмаған. Төмендегі пішінді толтырып, «⚡ Автобаптау» түймесін басыңыз.",
            [AppLanguage.English] = "No mailboxes connected yet. Fill out the form below and click \"⚡ Autodiscover\".",
            [AppLanguage.Chinese] = "暂未添加任何邮箱账户。请填写下方表单并点击“⚡ 自动检测配置”。"
        },
        ["SettingsAddAccountTitle"] = new()
        {
            [AppLanguage.Russian] = "＋ Подключить почтовый ящик (Автоопределение портов и протоколов)",
            [AppLanguage.Kazakh] = "＋ Пошта жәшігін қосу (Порттар мен хаттамаларды автоматты анықтау)",
            [AppLanguage.English] = "＋ Connect Mailbox (Auto-detect ports and protocols)",
            [AppLanguage.Chinese] = "＋ 添加邮箱账户（自动识别端口与协议）"
        },
        ["SettingsAutodiscoverBtn"] = new()
        {
            [AppLanguage.Russian] = "⚡ Автонастройка (Определить порты автоматически)",
            [AppLanguage.Kazakh] = "⚡ Автобаптау (Порттарды автоматты анықтау)",
            [AppLanguage.English] = "⚡ Autodiscover (Detect ports automatically)",
            [AppLanguage.Chinese] = "⚡ 自动检测配置（自动识别服务器端口）"
        },
        ["SettingsSaveAccountBtn"] = new()
        {
            [AppLanguage.Russian] = "💾 Проверить и сохранить почтовый ящик",
            [AppLanguage.Kazakh] = "💾 Пошта жәшігін тексеру және сақтау",
            [AppLanguage.English] = "💾 Verify and Save Mailbox",
            [AppLanguage.Chinese] = "💾 验证并保存邮箱配置"
        },
        ["SettingsEncryptionTitle"] = new()
        {
            [AppLanguage.Russian] = "🔐 Шифрование писем и фильтрация PixelGuard AV",
            [AppLanguage.Kazakh] = "🔐 Хаттарды шифрлау және PixelGuard AV сүзгісі",
            [AppLanguage.English] = "🔐 Message Encryption and PixelGuard AV Filtering",
            [AppLanguage.Chinese] = "🔐 邮件端到端加密与 PixelGuard AV 防护过滤"
        },
        ["SettingsBlacklistTitle"] = new()
        {
            [AppLanguage.Russian] = "🚫 Черный список (Заблокированные)",
            [AppLanguage.Kazakh] = "🚫 Қара тізім (Бұғатталғандар)",
            [AppLanguage.English] = "🚫 Blacklist (Blocked Senders)",
            [AppLanguage.Chinese] = "🚫 黑名单（已拦截发件人）"
        },
        ["SettingsWhitelistTitle"] = new()
        {
            [AppLanguage.Russian] = "✅ Белый список (Доверенные)",
            [AppLanguage.Kazakh] = "✅ Ақ тізім (Сенімділер)",
            [AppLanguage.English] = "✅ Whitelist (Trusted Senders)",
            [AppLanguage.Chinese] = "✅ 白名单（受信任发件人）"
        },
        ["MailWindowTitle"] = new()
        {
            [AppLanguage.Russian] = "Pixel Mail — Защищённый почтовый клиент (PixelGuard AV)",
            [AppLanguage.Kazakh] = "Pixel Mail — Қорғалған пошта клиенті (PixelGuard AV)",
            [AppLanguage.English] = "Pixel Mail — Secure Email Client (PixelGuard AV)",
            [AppLanguage.Chinese] = "Pixel Mail — 安全邮件客户端 (PixelGuard AV)"
        },
        ["MailComposeBtn"] = new()
        {
            [AppLanguage.Russian] = "✍️ Написать",
            [AppLanguage.Kazakh] = "✍️ Жазу",
            [AppLanguage.English] = "✍️ Compose",
            [AppLanguage.Chinese] = "✍️ 写邮件"
        },
        ["MailSyncBtn"] = new()
        {
            [AppLanguage.Russian] = "🔄 Проверить почту",
            [AppLanguage.Kazakh] = "🔄 Поштаны тексеру",
            [AppLanguage.English] = "🔄 Check Mail",
            [AppLanguage.Chinese] = "🔄 收取邮件"
        },
        ["MailReplyBtn"] = new()
        {
            [AppLanguage.Russian] = "↩️ Ответить",
            [AppLanguage.Kazakh] = "↩️ Жауап беру",
            [AppLanguage.English] = "↩️ Reply",
            [AppLanguage.Chinese] = "↩️ 回复"
        },
        ["MailForwardBtn"] = new()
        {
            [AppLanguage.Russian] = "↪️ Переслать",
            [AppLanguage.Kazakh] = "↪️ Қайта бағыттау",
            [AppLanguage.English] = "↪️ Forward",
            [AppLanguage.Chinese] = "↪️ 转发"
        },
        ["MailSettingsBtn"] = new()
        {
            [AppLanguage.Russian] = "⚙️ Настройки почты",
            [AppLanguage.Kazakh] = "⚙️ Пошта баптаулары",
            [AppLanguage.English] = "⚙️ Mail Settings",
            [AppLanguage.Chinese] = "⚙️ 邮箱设置"
        },
        ["MailOpenFileBtn"] = new()
        {
            [AppLanguage.Russian] = "📂 Открыть файл",
            [AppLanguage.Kazakh] = "📂 Файлды ашу",
            [AppLanguage.English] = "📂 Open File",
            [AppLanguage.Chinese] = "📂 打开文件"
        },
        ["MailSearchTooltip"] = new()
        {
            [AppLanguage.Russian] = "Поиск по теме, адресу, тексту или вложениям",
            [AppLanguage.Kazakh] = "Тақырып, мекенжай, мәтін немесе тіркемелер бойынша іздеу",
            [AppLanguage.English] = "Search by subject, sender, body, or attachments",
            [AppLanguage.Chinese] = "按主题、发件人、正文或附件搜索"
        },
        ["MailboxesHeader"] = new()
        {
            [AppLanguage.Russian] = "ПОЧТОВЫЕ ЯЩИКИ",
            [AppLanguage.Kazakh] = "ПОШТА ЖӘШІКТЕРІ",
            [AppLanguage.English] = "MAILBOXES",
            [AppLanguage.Chinese] = "邮箱账户"
        },
        ["AllMailboxes"] = new()
        {
            [AppLanguage.Russian] = "📬 Все ящики",
            [AppLanguage.Kazakh] = "📬 Барлық жәшіктер",
            [AppLanguage.English] = "📬 All Mailboxes",
            [AppLanguage.Chinese] = "📬 全部邮箱"
        },
        ["FoldersHeader"] = new()
        {
            [AppLanguage.Russian] = "ПАПКИ",
            [AppLanguage.Kazakh] = "ҚАЛТАЛАР",
            [AppLanguage.English] = "FOLDERS",
            [AppLanguage.Chinese] = "文件夹"
        },
        ["FolderUnifiedInbox"] = new()
        {
            [AppLanguage.Russian] = "📥 Все входящие",
            [AppLanguage.Kazakh] = "📥 Барлық кіріс",
            [AppLanguage.English] = "📥 Unified Inbox",
            [AppLanguage.Chinese] = "📥 全部收件箱"
        },
        ["FolderInbox"] = new()
        {
            [AppLanguage.Russian] = "📨 Входящие",
            [AppLanguage.Kazakh] = "📨 Кіріс хаттар",
            [AppLanguage.English] = "📨 Inbox",
            [AppLanguage.Chinese] = "📨 收件箱"
        },
        ["FolderSent"] = new()
        {
            [AppLanguage.Russian] = "📤 Отправленные",
            [AppLanguage.Kazakh] = "📤 Жіберілгендер",
            [AppLanguage.English] = "📤 Sent",
            [AppLanguage.Chinese] = "📤 已发送"
        },
        ["FolderDrafts"] = new()
        {
            [AppLanguage.Russian] = "📝 Черновики",
            [AppLanguage.Kazakh] = "📝 Нобайлар",
            [AppLanguage.English] = "📝 Drafts",
            [AppLanguage.Chinese] = "📝 草稿箱"
        },
        ["FolderSpam"] = new()
        {
            [AppLanguage.Russian] = "🛡️ Спам и Блокированные",
            [AppLanguage.Kazakh] = "🛡️ Спам және Бұғатталғандар",
            [AppLanguage.English] = "🛡️ Spam & Blocked",
            [AppLanguage.Chinese] = "🛡️ 垃圾与拦截邮件"
        },
        ["FolderTrash"] = new()
        {
            [AppLanguage.Russian] = "🗑️ Корзина",
            [AppLanguage.Kazakh] = "🗑️ Себет",
            [AppLanguage.English] = "🗑️ Trash",
            [AppLanguage.Chinese] = "🗑️ 已删除/废纸篓"
        },
        ["FolderLocalArchive"] = new()
        {
            [AppLanguage.Russian] = "💾 На компьютере (Архив)",
            [AppLanguage.Kazakh] = "💾 Компьютерде (Архив)",
            [AppLanguage.English] = "💾 Local PC Archive",
            [AppLanguage.Chinese] = "💾 本地存档"
        },
        ["ConnectMailboxQuick"] = new()
        {
            [AppLanguage.Russian] = "＋ Подключить ящик",
            [AppLanguage.Kazakh] = "＋ Жәшікті қосу",
            [AppLanguage.English] = "＋ Connect Mailbox",
            [AppLanguage.Chinese] = "＋ 添加邮箱"
        },
        ["FilterAll"] = new()
        {
            [AppLanguage.Russian] = "Все",
            [AppLanguage.Kazakh] = "Барлығы",
            [AppLanguage.English] = "All",
            [AppLanguage.Chinese] = "全部"
        },
        ["FilterUnread"] = new()
        {
            [AppLanguage.Russian] = "Непрочитанные",
            [AppLanguage.Kazakh] = "Оқылмағандар",
            [AppLanguage.English] = "Unread",
            [AppLanguage.Chinese] = "未读"
        },
        ["FilterAttachments"] = new()
        {
            [AppLanguage.Russian] = "📎 Вложения",
            [AppLanguage.Kazakh] = "📎 Тіркемелер",
            [AppLanguage.English] = "📎 Attachments",
            [AppLanguage.Chinese] = "📎 附件"
        },
        ["FilterEncrypted"] = new()
        {
            [AppLanguage.Russian] = "🔒 Шифр",
            [AppLanguage.Kazakh] = "🔒 Шифр",
            [AppLanguage.English] = "🔒 Encrypted",
            [AppLanguage.Chinese] = "🔒 加密邮件"
        },
        ["FilterThreats"] = new()
        {
            [AppLanguage.Russian] = "🛡️ Угрозы/Спам",
            [AppLanguage.Kazakh] = "🛡️ Қауіптер/Спам",
            [AppLanguage.English] = "🛡️ Threats/Spam",
            [AppLanguage.Chinese] = "🛡️ 威胁与垃圾"
        },
        ["MailContextMarkUnread"] = new()
        {
            [AppLanguage.Russian] = "✉️ Отметить как непрочитанное",
            [AppLanguage.Kazakh] = "✉️ Оқылмаған деп белгілеу",
            [AppLanguage.English] = "✉️ Mark as unread",
            [AppLanguage.Chinese] = "✉️ 标为未读"
        },
        ["MailContextMarkRead"] = new()
        {
            [AppLanguage.Russian] = "✉️ Отметить как прочитанное",
            [AppLanguage.Kazakh] = "✉️ Оқылған деп белгілеу",
            [AppLanguage.English] = "✉️ Mark as read",
            [AppLanguage.Chinese] = "✉️ 标为已读"
        },
        ["MailContextSpam"] = new()
        {
            [AppLanguage.Russian] = "🛡️ Отправить в спам",
            [AppLanguage.Kazakh] = "🛡️ Спамға жіберу",
            [AppLanguage.English] = "🛡️ Send to Spam",
            [AppLanguage.Chinese] = "🛡️ 移至垃圾箱"
        },
        ["MailContextDelete"] = new()
        {
            [AppLanguage.Russian] = "🗑️ Удалить",
            [AppLanguage.Kazakh] = "🗑️ Жою",
            [AppLanguage.English] = "🗑️ Delete",
            [AppLanguage.Chinese] = "🗑️ 删除"
        },
        ["AssistantStatusLabel"] = new()
        {
            [AppLanguage.Russian] = "Помощник: {0}",
            [AppLanguage.Kazakh] = "Көмекші: {0}",
            [AppLanguage.English] = "Assistant: {0}",
            [AppLanguage.Chinese] = "助手状态：{0}"
        },
        ["MessengerStatusLabel"] = new()
        {
            [AppLanguage.Russian] = "Мессенджер: {0}",
            [AppLanguage.Kazakh] = "Мессенджер: {0}",
            [AppLanguage.English] = "Messenger: {0}",
            [AppLanguage.Chinese] = "即时通讯：{0}"
        },
        ["LastServerSyncLabel"] = new()
        {
            [AppLanguage.Russian] = "Последняя связь: {0}",
            [AppLanguage.Kazakh] = "Соңғы байланыс: {0}",
            [AppLanguage.English] = "Last connection: {0}",
            [AppLanguage.Chinese] = "最后连通：{0}"
        }
    };
}
