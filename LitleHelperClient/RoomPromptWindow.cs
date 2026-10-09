using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PixelHelper;

public sealed class RoomPromptWindow : Window
{
    private readonly Settings settings;
    private readonly HubConnectionService hub;
    private readonly TextBox roomBox;
    private readonly TextBlock errorText;

    public RoomPromptWindow(Settings settings, HubConnectionService hub)
    {
        this.settings = settings;
        this.hub = hub;

        Title = "Укажите кабинет";
        Width = 440;
        Height = 260;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = true;
        Background = Brushes.Transparent;
        AllowsTransparency = true;
        WindowStyle = WindowStyle.None;

        var rootBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(71, 85, 105)), // Slate 600
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(16),
            Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)), // Slate 900
            Padding = new Thickness(20)
        };

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Prompt description
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Input box
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Error text
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Actions

        // Custom Header Bar
        var header = new Grid { Margin = new Thickness(0, 0, 0, 12), Background = Brushes.Transparent };
        header.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };

        var titleBlock = new TextBlock
        {
            Text = "🏢 Укажите номер кабинета",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(241, 245, 249)) // Slate 100
        };

        var closeBtn = new Button
        {
            Content = "✕",
            Width = 28,
            Height = 28,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            Cursor = Cursors.Hand
        };
        closeBtn.Click += (_, _) => Close();

        header.Children.Add(titleBlock);
        header.Children.Add(closeBtn);
        Grid.SetRow(header, 0);
        layout.Children.Add(header);

        // Description
        var descBlock = new TextBlock
        {
            Text = "Для точной маршрутизации заявок и вызова экстренных служб укажите номер вашего кабинета или поста:",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)), // Slate 300
            Margin = new Thickness(0, 0, 0, 12)
        };
        Grid.SetRow(descBlock, 1);
        layout.Children.Add(descBlock);

        // Input Box
        var inputBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)), // Slate 800
            BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85)), // Slate 700
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 0, 6)
        };

        roomBox = new TextBox
        {
            Text = settings.TicketRoom?.Trim() ?? "",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CaretBrush = Brushes.White,
            MaxLength = 50
        };
        roomBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) SaveAndClose();
            else if (e.Key == Key.Escape) Close();
        };

        inputBorder.Child = roomBox;
        Grid.SetRow(inputBorder, 2);
        layout.Children.Add(inputBorder);

        // Error text
        errorText = new TextBlock
        {
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113)), // Red 400
            Margin = new Thickness(0, 0, 0, 8),
            Visibility = Visibility.Collapsed
        };
        Grid.SetRow(errorText, 3);
        layout.Children.Add(errorText);

        // Actions
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom
        };

        var cancelBtn = new Button
        {
            Content = "Позже",
            Height = 34,
            Padding = new Thickness(16, 0, 16, 0),
            Margin = new Thickness(0, 0, 8, 0),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            Background = Brushes.Transparent,
            BorderBrush = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand
        };
        cancelBtn.Click += (_, _) => Close();

        var saveBtn = new Button
        {
            Content = "Сохранить",
            Height = 34,
            Padding = new Thickness(20, 0, 20, 0),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(37, 99, 235)), // Blue 600
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };
        saveBtn.Click += (_, _) => SaveAndClose();

        buttonPanel.Children.Add(cancelBtn);
        buttonPanel.Children.Add(saveBtn);
        Grid.SetRow(buttonPanel, 4);
        layout.Children.Add(buttonPanel);

        rootBorder.Child = layout;
        Content = rootBorder;

        Loaded += (_, _) =>
        {
            Activate();
            roomBox.Focus();
            roomBox.SelectAll();
        };
    }

    private void SaveAndClose()
    {
        string val = roomBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(val))
        {
            errorText.Text = "Пожалуйста, введите номер кабинета или название отделения";
            errorText.Visibility = Visibility.Visible;
            roomBox.Focus();
            return;
        }

        settings.TicketRoom = val;
        settings.Save();
        _ = hub.UpdateClientRoomAsync(val);
        DialogResult = true;
        Close();
    }
}
