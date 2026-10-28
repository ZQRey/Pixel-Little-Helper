using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace PixelHelper;

[Flags]
internal enum MessageStyle { None = 0, Bold = 1, Italic = 2, Underline = 4, Strike = 8, Code = 16 }
internal record MessageFragment(string Text, MessageStyle Style);
internal record MessageLine(IReadOnlyList<MessageFragment> Fragments, TextAlignment Alignment, bool CodeBlock = false);
internal record MessageEdit(string Text, int Start, int Length);

internal static class MessageFormatting
{
    internal const string Help = """
Выделите слово или фразу и нажмите сочетание клавиш.
Ctrl+B — жирный: **текст**
Ctrl+I — курсив: *текст*
Ctrl+U — подчёркнутый: __текст__
Ctrl+Shift+X — зачёркнутый: ~~текст~~
Ctrl+E — код в строке: `текст`
Ctrl+Shift+E — блок кода: тройные обратные кавычки на отдельных строках.

Повторное применение снимает оформление. Без выделения вставляются маркеры для ввода.
Стили можно сочетать; внутри кода они не применяются.
В поле ввода видна разметка, под ним — предпросмотр результата.
Enter — отправить; Shift+Enter — новая строка.
Esc — закрыть диалог, оставив мессенджер открытым; черновик сохраняется до закрытия мессенджера.

Выравнивание строк: Ctrl+Alt+L — влево, Ctrl+Alt+E — по центру, Ctrl+Alt+R — вправо. Добавьте Shift, чтобы применить ко всему сообщению.
Без выделения в режиме строк изменяется строка с курсором.
Выделение с переводом строки в конце не затрагивает следующую строку.
Выравнивание хранится как [align=center]текст[/align] или [align=right]текст[/align].
HTML-теги не исполняются. Максимальная длина вместе с разметкой — 4000 символов.
""";
    private static readonly (string Mark, MessageStyle Style)[] Marks =
        [("***", MessageStyle.Bold | MessageStyle.Italic), ("**", MessageStyle.Bold), ("__", MessageStyle.Underline), ("~~", MessageStyle.Strike), ("*", MessageStyle.Italic)];
    private static readonly Regex Alignment = new(@"^\[align=(left|center|right)\]([\s\S]*)\[/align\]$", RegexOptions.CultureInvariant);
    internal static IReadOnlyList<MessageLine> Parse(string text)
    {
        var result = new List<MessageLine>();
        bool fenced = false;
        foreach (string raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            string line = raw;
            var match = Alignment.Match(line);
            var align = TextAlignment.Left;
            if (match.Success) { align = match.Groups[1].Value switch { "center" => TextAlignment.Center, "right" => TextAlignment.Right, _ => TextAlignment.Left }; line = match.Groups[2].Value; }
            if (line == "```") { fenced = !fenced; continue; }
            result.Add(new(fenced ? [new(line, MessageStyle.Code)] : ParseInline(line), align, fenced));
        }
        return result;
    }
    internal static IReadOnlyList<MessageFragment> ParseInline(string text, MessageStyle style = MessageStyle.None, int depth = 0)
    {
        if (depth >= 8) return [new(text, style)];
        var result = new List<MessageFragment>();
        var plain = new StringBuilder();
        void Flush() { if (plain.Length > 0) { result.Add(new(plain.ToString(), style)); plain.Clear(); } }
        for (int i = 0; i < text.Length;)
        {
            if (text[i] == '\\' && i + 1 < text.Length && "\\*`_~[]".Contains(text[i + 1])) { plain.Append(text[i + 1]); i += 2; continue; }
            if (text[i] == '`')
            {
                int end = text.IndexOf('`', i + 1);
                if (end > i + 1) { Flush(); result.Add(new(text[(i + 1)..end], style | MessageStyle.Code)); i = end + 1; continue; }
            }
            bool matched = false;
            foreach (var (mark, flag) in Marks)
            {
                if (!text.AsSpan(i).StartsWith(mark, StringComparison.Ordinal)) continue;
                int end = i + mark.Length;
                do { end = text.IndexOf(mark, end, StringComparison.Ordinal); if (end < 0 || end == 0 || text[end - 1] != '\\') break; end += mark.Length; } while (true);
                if (end <= i + mark.Length) continue;
                Flush(); result.AddRange(ParseInline(text[(i + mark.Length)..end], style | flag, depth + 1));
                i = end + mark.Length; matched = true; break;
            }
            if (!matched) plain.Append(text[i++]);
        }
        Flush();
        return result;
    }
    internal static MessageEdit Toggle(string text, int start, int length, string marker)
    {
        if (marker == "```")
        {
            int from = start == 0 ? 0 : text.LastIndexOf('\n', start - 1) + 1;
            int end = start + length;
            if (length > 0 && text[end - 1] == '\n') end--;
            int to = text.IndexOf('\n', end);
            if (to < 0) to = text.Length;
            start = from; length = to - from;
        }
        string selected = text.Substring(start, length), open = marker == "```" ? "```\n" : marker, close = marker == "```" ? "\n```" : marker;
        int replaceStart = start, replaceLength = length;
        string replacement;
        int caret;
        bool italicSelected = marker != "*" || !selected.StartsWith("**") || selected.StartsWith("***");
        int beforeStars = 0, afterStars = 0;
        for (int i = start - 1; i >= 0 && text[i] == '*'; i--) beforeStars++;
        for (int i = start + length; i < text.Length && text[i] == '*'; i++) afterStars++;
        bool italicSurround = marker != "*" || beforeStars % 2 == 1 && afterStars % 2 == 1;
        if (italicSelected && length >= open.Length + close.Length && selected.StartsWith(open) && selected.EndsWith(close))
        { replacement = selected[open.Length..^close.Length]; caret = start; }
        else if (italicSurround && start >= open.Length && text.AsSpan(start - open.Length, open.Length).SequenceEqual(open) && start + length + close.Length <= text.Length && text.AsSpan(start + length, close.Length).SequenceEqual(close))
        { replaceStart -= open.Length; replaceLength += open.Length + close.Length; replacement = selected; caret = replaceStart; }
        else { replacement = open + selected + close; caret = start + open.Length; }
        string next = text.Remove(replaceStart, replaceLength).Insert(replaceStart, replacement);
        if (next.Length > 4000) throw new ArgumentException("Сообщение вместе с разметкой не должно превышать 4000 символов.");
        return new(next, caret, length == 0 ? 0 : (replacement.StartsWith(open) ? selected.Length : replacement.Length));
    }
    internal static MessageEdit Align(string text, int start, int length, string alignment, bool all)
    {
        if (alignment is not ("left" or "center" or "right")) throw new ArgumentException("Неверное выравнивание.");
        int from = all || start == 0 ? 0 : text.LastIndexOf('\n', start - 1) + 1;
        int last = start + length;
        if (length > 0 && last > 0 && text[last - 1] == '\n') last--;
        int to = all ? text.Length : text.IndexOf('\n', last);
        if (to < 0) to = text.Length;
        string changed = string.Join("\n", text[from..to].Split('\n').Select(line => {
            string cr = line.EndsWith('\r') ? "\r" : "";
            string content = cr.Length > 0 ? line[..^1] : line;
            var match = Alignment.Match(content);
            if (match.Success) content = match.Groups[2].Value;
            return (alignment == "left" ? content : "[align=" + alignment + "]" + content + "[/align]") + cr;
        }));
        string next = text[..from] + changed + text[to..];
        if (next.Length > 4000) throw new ArgumentException("Сообщение вместе с разметкой не должно превышать 4000 символов.");
        return new(next, from, changed.Length);
    }
    internal static StackPanel Render(string text, double emojiSize, bool animated, double fontSize, Brush foreground)
    {
        var panel = new StackPanel();
        foreach (var line in Parse(text))
        {
            var block = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = fontSize, Foreground = foreground, TextAlignment = line.Alignment, MinHeight = fontSize * 1.25 };
            foreach (var part in line.Fragments)
            {
                var span = new Span();
                if (part.Style.HasFlag(MessageStyle.Bold)) span.FontWeight = FontWeights.Bold;
                if (part.Style.HasFlag(MessageStyle.Italic)) span.FontStyle = FontStyles.Italic;
                var decorations = new TextDecorationCollection();
                if (part.Style.HasFlag(MessageStyle.Underline)) decorations.Add(TextDecorations.Underline);
                if (part.Style.HasFlag(MessageStyle.Strike)) decorations.Add(TextDecorations.Strikethrough);
                span.TextDecorations = decorations;
                if (part.Style.HasFlag(MessageStyle.Code)) { span.FontFamily = new FontFamily("Consolas"); span.Background = new SolidColorBrush(Color.FromArgb(35, 128, 128, 128)); span.Inlines.Add(new Run(part.Text)); }
                else
                {
                    var emojis = HelperEmojis.Render(part.Text, emojiSize, animated);
                    while (emojis.Inlines.FirstInline is Inline inline) { emojis.Inlines.Remove(inline); span.Inlines.Add(inline); }
                }
                block.Inlines.Add(span);
            }
            panel.Children.Add(block);
        }
        if (Parse(text).Any(line => line.Alignment != TextAlignment.Left)) panel.MinWidth = 220;
        return panel;
    }

    internal static bool TryApplyShortcut(TextBox box, System.Windows.Input.KeyEventArgs e)
    {
        var mod = System.Windows.Input.Keyboard.Modifiers;
        bool ctrl = (mod & System.Windows.Input.ModifierKeys.Control) != 0;
        bool shift = (mod & System.Windows.Input.ModifierKeys.Shift) != 0;
        bool alt = (mod & System.Windows.Input.ModifierKeys.Alt) != 0;
        if (!ctrl) return false;

        try
        {
            MessageEdit? edit = null;
            if (!alt && !shift && e.Key == System.Windows.Input.Key.B) edit = Toggle(box.Text, box.SelectionStart, box.SelectionLength, "**");
            else if (!alt && !shift && e.Key == System.Windows.Input.Key.I) edit = Toggle(box.Text, box.SelectionStart, box.SelectionLength, "*");
            else if (!alt && !shift && e.Key == System.Windows.Input.Key.U) edit = Toggle(box.Text, box.SelectionStart, box.SelectionLength, "__");
            else if (!alt && shift && e.Key == System.Windows.Input.Key.X) edit = Toggle(box.Text, box.SelectionStart, box.SelectionLength, "~~");
            else if (!alt && !shift && e.Key == System.Windows.Input.Key.E) edit = Toggle(box.Text, box.SelectionStart, box.SelectionLength, "");
            else if (!alt && shift && e.Key == System.Windows.Input.Key.E) edit = Toggle(box.Text, box.SelectionStart, box.SelectionLength, "`");
            else if (alt && e.Key == System.Windows.Input.Key.L) edit = Align(box.Text, box.SelectionStart, box.SelectionLength, "left", shift);
            else if (alt && e.Key == System.Windows.Input.Key.E) edit = Align(box.Text, box.SelectionStart, box.SelectionLength, "center", shift);
            else if (alt && e.Key == System.Windows.Input.Key.R) edit = Align(box.Text, box.SelectionStart, box.SelectionLength, "right", shift);

            if (edit != null)
            {
                box.Text = edit.Text;
                box.Select(edit.Start, edit.Length);
                return true;
            }
        }
        catch { }
        return false;
    }
}