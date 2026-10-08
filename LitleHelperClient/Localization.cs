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
        }
    };
}
