using System.Text.RegularExpressions;

namespace PixelHelper;

public enum CompanionBotAction
{
    None,
    ClearHistory,
    CryReaction,
    ChangePassword
}

public enum CompanionEmotion
{
    Normal,
    Greeting,
    Joy,
    Sad,
    Laugh,
    Think,
    Cry
}

internal sealed record CompanionBotResult(
    string ReplyText,
    CompanionEmotion Emotion,
    CompanionBotAction Action = CompanionBotAction.None,
    PetState RobotState = PetState.Idle,
    bool IsPromptingPassword = false,
    string? TargetPassword = null
);

internal static class CompanionBotEngine
{
    public static readonly DateTime DefaultOriginDate = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    public static string FormatAge(TimeSpan age, string? langCode = null)
    {
        string lang = langCode ?? Loc.Code ?? "ru";
        int totalDays = Math.Max(0, (int)age.TotalDays);

        int years = totalDays / 365;
        int remainingDays = totalDays % 365;
        int months = remainingDays / 30;
        int days = remainingDays % 30;

        return lang switch
        {
            "kk" => FormatAgeKk(years, months, days, totalDays),
            "en" => FormatAgeEn(years, months, days, totalDays),
            "zh" => FormatAgeZh(years, months, days, totalDays),
            _ => FormatAgeRu(years, months, days, totalDays)
        };
    }

    private static string FormatAgeRu(int years, int months, int days, int totalDays)
    {
        if (totalDays == 0) return "меньше одного дня";
        if (years > 0)
        {
            string yStr = Declension(years, "год", "года", "лет");
            if (months > 0)
                return $"{years} {yStr} и {months} {Declension(months, "месяц", "месяца", "месяцев")}";
            return $"{years} {yStr}";
        }
        if (months > 0)
        {
            string mStr = Declension(months, "месяц", "месяца", "месяцев");
            if (days > 0)
                return $"{months} {mStr} и {days} {Declension(days, "день", "дня", "дней")}";
            return $"{months} {mStr}";
        }
        return $"{days} {Declension(days, "день", "дня", "дней")}";
    }

    private static string FormatAgeKk(int years, int months, int days, int totalDays)
    {
        if (totalDays == 0) return "бір күннен аз";
        if (years > 0)
        {
            if (months > 0) return $"{years} жыл және {months} ай";
            return $"{years} жыл";
        }
        if (months > 0)
        {
            if (days > 0) return $"{months} ай және {days} күн";
            return $"{months} ай";
        }
        return $"{days} күн";
    }

    private static string FormatAgeEn(int years, int months, int days, int totalDays)
    {
        if (totalDays == 0) return "less than a day";
        if (years > 0)
        {
            string yStr = years == 1 ? "1 year" : $"{years} years";
            if (months > 0)
                return $"{yStr} and {(months == 1 ? "1 month" : $"{months} months")}";
            return yStr;
        }
        if (months > 0)
        {
            string mStr = months == 1 ? "1 month" : $"{months} months";
            if (days > 0)
                return $"{mStr} and {(days == 1 ? "1 day" : $"{days} days")}";
            return mStr;
        }
        return days == 1 ? "1 day" : $"{days} days";
    }

    private static string FormatAgeZh(int years, int months, int days, int totalDays)
    {
        if (totalDays == 0) return "不到1天";
        if (years > 0)
        {
            if (months > 0) return $"{years}年{months}个月";
            return $"{years}年";
        }
        if (months > 0)
        {
            if (days > 0) return $"{months}个月{days}天";
            return $"{months}个月";
        }
        return $"{days}天";
    }

    private static string Declension(int number, string one, string two, string five)
    {
        int n = Math.Abs(number) % 100;
        int n1 = n % 10;
        if (n is > 10 and < 20) return five;
        if (n1 > 1 && n1 < 5) return two;
        if (n1 == 1) return one;
        return five;
    }

    public static CompanionBotResult GenerateResponse(string input, DateTime? originDate = null, bool isAwaitingPassword = false)
    {
        string raw = (input ?? "").Trim();
        string lower = raw.ToLowerInvariant();
        string lang = Loc.Code ?? "ru";

        if (string.IsNullOrWhiteSpace(raw))
        {
            return new CompanionBotResult(
                Loc.T("CompanionUnknownResponse"),
                CompanionEmotion.Think,
                CompanionBotAction.None,
                PetState.Think
            );
        }

        // 1. Check Forget / Memory wipe command
        if (IsForgetCommand(lower))
        {
            CompanionStorage.Clear();
            return new CompanionBotResult(
                Loc.T("CompanionForgetResponse"),
                CompanionEmotion.Joy,
                CompanionBotAction.ClearHistory,
                PetState.Joy
            );
        }

        // 1b. Check Pet Animal command (/pet, /погладить, /питомец)
        if (lower is "/pet" or "/погладить" or "/питомец" || lower.StartsWith("/pet ") || lower.StartsWith("/погладить ") || lower.StartsWith("/питомец "))
        {
            bool isCat = Random.Shared.Next(2) == 0;
            return new CompanionBotResult(
                Loc.T(isCat ? "NoticePetCat" : "NoticePetDog"),
                CompanionEmotion.Joy,
                CompanionBotAction.None,
                isCat ? PetState.PetCat : PetState.PetDog
            );
        }

        // 2. Check Insult / Toxicity
        if (IsInsult(lower))
        {
            DateTime origin = originDate ?? DefaultOriginDate;
            TimeSpan age = DateTime.UtcNow - origin;
            if (age < TimeSpan.Zero) age = TimeSpan.Zero;
            string ageFormatted = FormatAge(age, lang);
            string hurtText = Loc.Format("CompanionHurtResponse", ageFormatted);

            return new CompanionBotResult(
                hurtText,
                CompanionEmotion.Cry,
                CompanionBotAction.CryReaction,
                PetState.Cry
            );
        }

        // 3. Password flow handling
        if (isAwaitingPassword)
        {
            if (IsCancelCommand(lower))
            {
                string cancelText = lang switch
                {
                    "kk" => "Жарайды, құпия сөзді ауыстыру тоқтатылды. Көмек керек болса, жаза ғой!",
                    "en" => "Password change cancelled. Let me know if you need any help!",
                    "zh" => "好的，已取消修改密码。如需帮助随时告诉我！",
                    _ => "Хорошо, смена пароля отменена. Если понадобится помощь — пиши!"
                };
                return new CompanionBotResult(cancelText, CompanionEmotion.Normal, CompanionBotAction.None, PetState.Idle, IsPromptingPassword: false);
            }

            if (IsSuggestionCommand(lower))
            {
                var suggestions = MnemonicPasswordGenerator.Generate(3);
                while (suggestions.Count < 3) suggestions.Add("Solnce2026!");
                string text = Loc.Format("PasswordSuggestionsHeader", suggestions[0], suggestions[1], suggestions[2]);
                return new CompanionBotResult(text, CompanionEmotion.Think, CompanionBotAction.None, PetState.Think, IsPromptingPassword: true);
            }

            // Candidate password provided
            string candidate = raw.Trim().TrimStart('•', '-', '*', ' ').Trim();
            var (isValid, errorMessage) = PasswordPolicyValidator.Validate(candidate, lang);
            if (!isValid)
            {
                string errorMsg = $"{errorMessage}\n\n{Loc.T("PasswordChangePrompt")}";
                return new CompanionBotResult(errorMsg, CompanionEmotion.Sad, CompanionBotAction.None, PetState.Sad, IsPromptingPassword: true);
            }

            string proceedText = lang switch
            {
                "kk" => "Тамаша құпия сөз! Оны жүйеде өзгертудемін...",
                "en" => "Password meets all requirements! Updating your password in Active Directory...",
                "zh" => "密码强度符合要求！正在为您在活动目录中修改密码...",
                _ => "Отличный пароль! Отправляю запрос на изменение пароля в домене..."
            };
            return new CompanionBotResult(proceedText, CompanionEmotion.Think, CompanionBotAction.ChangePassword, PetState.Think, IsPromptingPassword: false, TargetPassword: candidate);
        }

        // If not already awaiting, check if user asks for portal or general password generation
        if (IsGeneralPasswordQuery(lower) && !MatchesExplicitChangeIntent(lower))
        {
            var suggestions = MnemonicPasswordGenerator.Generate(3);
            while (suggestions.Count < 3) suggestions.Add("Solnce2026!");
            string text = Loc.Format("PasswordGeneralSuggestions", suggestions[0], suggestions[1], suggestions[2]);
            return new CompanionBotResult(text, CompanionEmotion.Joy, CompanionBotAction.None, PetState.Joy, IsPromptingPassword: false);
        }

        // Check if user asks to change Windows / AD domain password
        if (MatchesPasswordIntent(lower))
        {
            var match = Regex.Match(raw, @"(?:пароль\s+на|password\s+to|сөзді\s+|密码为\s*|:\s*)([^\s]+)", RegexOptions.IgnoreCase);
            if (match.Success && !string.IsNullOrWhiteSpace(match.Groups[1].Value))
            {
                string candidate = match.Groups[1].Value.Trim().TrimStart('•', '-', '*', ' ').Trim();
                var (isValid, errorMessage) = PasswordPolicyValidator.Validate(candidate, lang);
                if (!isValid)
                {
                    string errorMsg = $"{errorMessage}\n\n{Loc.T("PasswordChangePrompt")}";
                    return new CompanionBotResult(errorMsg, CompanionEmotion.Sad, CompanionBotAction.None, PetState.Sad, IsPromptingPassword: true);
                }

                string proceedText = lang switch
                {
                    "kk" => "Тамаша құпия сөз! Оны жүйеде өзгертудемін...",
                    "en" => "Password meets all requirements! Updating your password in Active Directory...",
                    "zh" => "密码强度符合要求！正在为您在活动目录中修改密码...",
                    _ => "Отличный пароль! Отправляю запрос на изменение пароля в домене..."
                };
                return new CompanionBotResult(proceedText, CompanionEmotion.Think, CompanionBotAction.ChangePassword, PetState.Think, IsPromptingPassword: false, TargetPassword: candidate);
            }

            return new CompanionBotResult(Loc.T("PasswordChangePrompt"), CompanionEmotion.Greeting, CompanionBotAction.None, PetState.Think, IsPromptingPassword: true);
        }

        // 4. Empathetic categories
        // Sadness / grief
        if (MatchesAny(lower, "грустн", "печал", "одинок", "плак", "плохо", "тоск", "мұң", "жыла", "жабырқау", "sad", "unhappy", "lonely", "crying", "depress", "难过", "伤心", "孤独", "想哭"))
        {
            string reply = lang switch
            {
                "kk" => "Мен қасыңдамын. Тіпті ең мықты жандардың да ауыр күндері болады. Сен қолыңнан келгеннің бәрін жасап жатырсың және сен өте құндысың. Сыр бөліскің келе ме? Мен тыңдап тұрмын.",
                "en" => "I'm right here with you. Even the strongest people have difficult days. You are doing your best, and you truly matter. Do you want to talk it through? I'm listening.",
                "zh" => "我就在你身边。即使是最坚强的人也会有难过的时候。你已经尽力了，你非常重要。想跟我倾诉一下吗？我在认真听着。",
                _ => "Я рядом с тобой. Даже у самых сильных людей бывают тяжелые дни. Ты делаешь всё, что можешь, и ты ценен. Хочешь выговориться? Я слушаю."
            };
            return new CompanionBotResult(reply, CompanionEmotion.Sad, CompanionBotAction.None, PetState.Sad);
        }

        // Fatigue / Stress / Burnout
        if (MatchesAny(lower, "устал", "выгор", "завал", "дедлайн", "начальник", "тяжело", "не вывожу", "шарша", "қиын", "tired", "burnout", "exhausted", "stress", "deadline", "hard work", "累", "疲惫", "压力", "加班"))
        {
            string reply = lang switch
            {
                "kk" => "Бірнеше минутқа демалып ал. Көзіңді жұмып, терең тыныс ал. Жұмыс маңызды, бірақ денсаулығың мен тыныштығың бәрінен де қымбат. Біз бәрін асықпай шешеміз.",
                "en" => "Take a breath and pause for a few minutes. Close your eyes and breathe deeply. Work is important, but your peace and well-being come first. Drink some water, we'll take it step by step.",
                "zh" => "停下来休息几分钟吧。闭上双眼，慢慢深呼吸。工作固然重要，但你的健康和宁静才是第一位的。喝杯温水，我们会一步一步度过的。",
                _ => "Сделай паузу на пару минут. Закрой глаза, сделай медленный глубокий вдох. Работа важна, но твоё здоровье и покой важнее всего. Выпей воды, разомни плечи. Мы справимся по шагам."
            };
            return new CompanionBotResult(reply, CompanionEmotion.Think, CompanionBotAction.None, PetState.Think);
        }

        // Joy / Success
        if (MatchesAny(lower, "ура", "получилось", "повысил", "счастлив", "побед", "радост", "алақай", "сәтті", "қуаныш", "hooray", "success", "awesome", "happy", "promoted", "yay", "太棒了", "成功", "开心", "真棒"))
        {
            string reply = lang switch
            {
                "kk" => "Алақай! Мен сен үшін шексіз қуаныштымын! Еңбегің өз жемісін берді! Сен нағыз жарайсың! 🌟",
                "en" => "Hooray! I am so happy for you! Your hard work is paying off! You're amazing! 🌟",
                "zh" => "太棒啦！我真为你感到高兴！你的努力终于得到了回报！你太了不起了！🌟",
                _ => "Ура-а-а! Я так рад за тебя! Твои старания принесли плоды! Ты большой молодец! 🌟"
            };
            return new CompanionBotResult(reply, CompanionEmotion.Joy, CompanionBotAction.None, PetState.Joy);
        }

        // Jokes / Humor
        if (MatchesAny(lower, "шутк", "анекдот", "рассмеши", "пошути", "юмор", "әзіл", "қалжың", "joke", "funny", "laugh", "笑话", "讲个笑话"))
        {
            string reply = lang switch
            {
                "kk" => "Робот неге күндіз ұйықтамайды? Себебі оның армандары да кодталған! 🤖😄 Ал егер байқасаң, әрбір қателік — бұл тек құжатталмаған ерекшелік қана!",
                "en" => "Why do programmers prefer dark mode? Because light attracts bugs! 🤖😄 Remember: there are 10 types of people in the world — those who understand binary, and those who don't!",
                "zh" => "为什么程序员喜欢深色模式？因为光会吸引Bug！🤖😄 记住：世界上有10种人，一种是懂二进制的，另一种是不懂的！",
                _ => "Почему программисты любят тёмную тему? Потому что свет притягивает баги! 🤖😄 А ещё помни: если код работает с первого раза — это не чудо, а повод насторожиться!"
            };
            return new CompanionBotResult(reply, CompanionEmotion.Laugh, CompanionBotAction.None, PetState.Laugh);
        }

        // Greeting / Who are you / Friendship
        if (MatchesAny(lower, "привет", "здравствуй", "кто ты", "как дела", "мы друзья", "ты настоящий", "сәлем", "сен кімсің", "дос", "hello", "hi", "who are you", "friend", "how are you", "你好", "你是谁", "朋友"))
        {
            string reply = lang switch
            {
                "kk" => "Сәлем! Мен сенің кішкентай пиксельді досыңмын. Мен әрдайым экранның шетіндемін және саған көмектесуге немесе жай ғана әңгімелесуге дайынмын! Біз әрқашан доспыз. ✨",
                "en" => "Hello! I am your little pixel companion. I live right here on your screen, always ready to assist or just keep you company. We are definitely friends! ✨",
                "zh" => "你好！我是你的像素小助手伙伴。我一直就在你的屏幕角落陪伴着你，无论你需要帮助还是想聊聊天，我随时都在！我们永远是好朋友！✨",
                _ => "Привет! Я твой маленький пиксельный друг. Я живу прямо здесь на экране, всегда готов подбодрить тебя, выслушать или помочь в делах. Мы обязательно друзья! ✨"
            };
            return new CompanionBotResult(reply, CompanionEmotion.Greeting, CompanionBotAction.None, PetState.Greeting);
        }

        // Fallback
        return new CompanionBotResult(
            Loc.T("CompanionUnknownResponse"),
            CompanionEmotion.Think,
            CompanionBotAction.None,
            PetState.Think
        );
    }

    private static bool IsForgetCommand(string lower)
    {
        return MatchesAny(lower,
            "забудь всё", "забудь все", "забудь о чем", "забудь о чём",
            "сотри переписку", "сотри все", "сотри всё", "очисти память", "очисти историю", "забудь меня",
            "бәрін ұмыт", "ұмыт бәрін", "жадыны тазала", "өткенді ұмыт",
            "forget everything", "clear memory", "clear chat", "delete history", "erase memory", "forget all",
            "全部忘记", "清空记忆", "清除记录", "忘了我", "忘记一切");
    }

    private static bool IsInsult(string lower)
    {
        return MatchesAny(lower,
            "дурак", "тупой", "тупая", "тупое", "урод", "бесишь", "заткнись", "идиот", "мразь", "ненавижу тебя", "тварь", "отвали", "дебил", "придурок",
            "ақымақ", "жынды", "кетші", "өшір", "жек көрем", "оңбаған",
            "stupid", "idiot", "shut up", "hate you", "ugly", "dumb", "jerk", "moron", "useless bot",
            "笨蛋", "傻瓜", "白痴", "闭嘴", "讨厌你", "滚开", "废物");
    }

    private static bool IsCancelCommand(string lower)
    {
        return MatchesAny(lower, "отмена", "отменить", "не надо", "передумал", "отбой", "болдырмау", "керек емес", "cancel", "stop", "abort", "nevermind", "取消", "不用了", "算了");
    }

    private static bool IsSuggestionCommand(string lower)
    {
        return MatchesAny(lower,
            "придумай", "не знаю какой", "не знаю", "предложи", "сгенерируй", "посоветуй", "какой поставить", "вариант",
            "ойлап тап", "ұсын", "қандай қояйын", "білмеймін",
            "suggest", "generate", "don't know", "dont know", "recommend", "make one up", "give me idea",
            "帮我想", "生成密码", "不知道", "推荐", "想一个");
    }

    private static bool MatchesPasswordIntent(string lower)
    {
        return MatchesAny(lower,
            "поменяй пароль", "поменять пароль", "смени пароль", "сменить пароль", "новый пароль", "сбрось пароль", "сбросить пароль", "забыл пароль", "как поменять пароль", "как сменить пароль",
            "құпия сөз", "пароль ауыстыр", "парольді ауыстыр", "пароль өзгерту", "жаңа құпия сөз",
            "change password", "reset password", "new password", "how to change password", "update password",
            "修改密码", "更改密码", "换密码", "重置密码", "新密码");
    }

    private static bool MatchesExplicitChangeIntent(string lower)
    {
        return MatchesAny(lower,
            "поменяй пароль", "поменять пароль", "смени пароль", "сменить пароль", "сбрось пароль", "сбросить пароль", "как поменять пароль", "как сменить пароль",
            "пароль ауыстыр", "парольді ауыстыр", "пароль өзгерту",
            "change password", "reset password", "how to change password", "update password",
            "修改密码", "更改密码", "换密码", "重置密码");
    }

    private static bool IsGeneralPasswordQuery(string lower)
    {
        return MatchesAny(lower,
            "портал", "сайт", "почт", "сервис", "аккаунт", "придумай пароль", "сгенерируй пароль", "мне нужен пароль", "нужен пароль", "сложный пароль", "надежный пароль", "вариант пароля", "пароль для", "создай пароль",
            "құпия сөз ойлап тап", "құпия сөз керек", "жаңа пароль ойлап",
            "portal", "website", "service", "generate password", "create password", "need a password", "suggest password", "strong password", "make a password",
            "门户", "网站", "生成密码", "推荐密码", "需要密码", "想个密码", "创建密码");
    }

    private static bool MatchesAny(string input, params string[] patterns)
    {
        foreach (var p in patterns)
        {
            if (input.Contains(p, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
