namespace PixelHelper;

public static class PasswordPolicyValidator
{
    public static (bool IsValid, string Message) Validate(string? password, string lang = "ru")
    {
        if (string.IsNullOrEmpty(password))
        {
            string emptyMsg = lang switch
            {
                "kk" => "Пароль бос болмауы керек.",
                "en" => "Password cannot be empty.",
                "zh" => "密码不能为空。",
                _ => "Пароль не может быть пустым."
            };
            return (false, emptyMsg);
        }

        if (password.Length < 8)
        {
            string lenMsg = lang switch
            {
                "kk" => "Пароль кемінде 8 таңбадан тұруы керек.",
                "en" => "Password must be at least 8 characters long.",
                "zh" => "密码长度必须至少为8个字符。",
                _ => "Пароль должен содержать не менее 8 символов."
            };
            return (false, lenMsg);
        }

        bool hasUpper = password.Any(char.IsUpper);
        bool hasLower = password.Any(char.IsLower);
        bool hasDigit = password.Any(char.IsDigit);

        if (!hasUpper || !hasLower || !hasDigit)
        {
            var missing = new List<string>();
            if (!hasUpper)
            {
                missing.Add(lang switch { "kk" => "бас әріптер (A-Z/А-Я)", "en" => "uppercase letters (A-Z)", "zh" => "大写字母", _ => "заглавные буквы (A-Z/А-Я)" });
            }
            if (!hasLower)
            {
                missing.Add(lang switch { "kk" => "кіші әріптер (a-z/а-я)", "en" => "lowercase letters (a-z)", "zh" => "小写字母", _ => "строчные буквы (a-z/а-я)" });
            }
            if (!hasDigit)
            {
                missing.Add(lang switch { "kk" => "сандар (0-9)", "en" => "digits (0-9)", "zh" => "数字", _ => "цифры (0-9)" });
            }

            string combined = string.Join(", ", missing);
            string complexMsg = lang switch
            {
                "kk" => $"Парольде мыналар жетіспейді: {combined}. Пароль талаптары: кемінде 8 таңба, бас және кіші әріптер, сандар.",
                "en" => $"Password is missing: {combined}. Requirements: at least 8 characters, uppercase and lowercase letters, and digits.",
                "zh" => $"密码缺少：{combined}。要求：至少8个字符，包含大小写字母和数字。",
                _ => $"В пароле не хватает: {combined}. Требования: не менее 8 символов, заглавные и строчные буквы, цифры."
            };
            return (false, complexMsg);
        }

        string okMsg = lang switch
        {
            "kk" => "Пароль қауіпсіздік талаптарына сәйкес келеді.",
            "en" => "Password meets security requirements.",
            "zh" => "密码符合安全策略。",
            _ => "Пароль соответствует политике безопасности."
        };
        return (true, okMsg);
    }
}
