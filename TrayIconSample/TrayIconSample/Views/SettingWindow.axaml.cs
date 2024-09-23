using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace TrayIconSample.Views
{
    public partial class SettingWindow : Window
    {
        public SettingWindow()
        {
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
            // 設定の読み込み
            LoadSettings();
        }

        /// <summary>
        /// 設定を読み込んでUIに反映します。
        /// 実際の設定ストレージから値を取得するように実装してください。
        /// </summary>
        private void LoadSettings()
        {
            // TODO: 実際の設定ストレージから値を読み込む
            // NotificationsCheckBox.IsChecked = true; // 例: 通知を有効化
            // UsernameTextBox.Text = "User123"; // 例: デフォルトのユーザー名
            // ThemeComboBox.SelectedIndex = 0; // 例: Lightテーマを選択
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // UIから値を取得
            // bool notificationsEnabled = NotificationsCheckBox.IsChecked ?? false;
            // string username = UsernameTextBox.Text;
            // string selectedTheme = (ThemeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();

            // TODO: 取得した値を設定ストレージに保存する
            // Settings.NotificationsEnabled = notificationsEnabled;
            // Settings.Username = username;
            // Settings.Theme = selectedTheme;
            // Settings.Save();

            // ウィンドウを閉じる
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            // 変更を保存せずにウィンドウを閉じる
            Close();
        }
    }
}
