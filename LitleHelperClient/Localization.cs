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
        }
    };
}
