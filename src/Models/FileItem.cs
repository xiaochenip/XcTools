using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace XcTools.Models
{
    public class FileItem : INotifyPropertyChanged
    {
        private string _fullPath;
        private string _name;
        private string _size;
        private string _lastWriteTime;
        private BitmapImage _thumbnail;
        private string _category;

        public string FullPath
        {
            get => _fullPath;
            set { _fullPath = value; OnPropertyChanged(); }
        }

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public string DisplayName => System.IO.Path.GetFileNameWithoutExtension(Name);

        public string Size
        {
            get => _size;
            set { _size = value; OnPropertyChanged(); }
        }

        public string LastWriteTime
        {
            get => _lastWriteTime;
            set { _lastWriteTime = value; OnPropertyChanged(); }
        }

        public BitmapImage Thumbnail
        {
            get => _thumbnail;
            set { _thumbnail = value; OnPropertyChanged(); }
        }

        public string Category
        {
            get => _category;
            set { _category = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
