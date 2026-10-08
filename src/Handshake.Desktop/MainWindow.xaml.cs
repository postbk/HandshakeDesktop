using Microsoft.Win32;
using System.Collections.ObjectModel;

namespace Handshake.Desktop;

public partial class MainWindow : System.Windows.Window
{
    private readonly ObservableCollection<string> _selectedImages = [];

    public MainWindow()
    {
        InitializeComponent();
        ImagesList.ItemsSource = _selectedImages;
    }

    private void ChooseImages_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select business cards or company documents",
            Filter = "Images|*.jpg;*.jpeg;*.png;*.webp;*.bmp;*.tif;*.tiff|All files|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog() != true) return;
        foreach (var file in dialog.FileNames.Take(4))
            if (!_selectedImages.Contains(file)) _selectedImages.Add(file);
        StatusText.Text = $"{_selectedImages.Count} image(s) selected. Review them before extraction.";
    }

    private void GooglePhotos_Click(object sender, System.Windows.RoutedEventArgs e) =>
        System.Windows.MessageBox.Show("Google Photos Picker will be connected in the next increment. It will use explicit user-selected photos rather than broad library access.", "Google Photos");

    private void NewEvent_Click(object sender, System.Windows.RoutedEventArgs e) =>
        System.Windows.MessageBox.Show("Event creation is the next database-backed increment.", "New event");

    private void Settings_Click(object sender, System.Windows.RoutedEventArgs e) =>
        System.Windows.MessageBox.Show("Settings will hold the encrypted OpenAI key and Google account connection.", "Settings");
}
