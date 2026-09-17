using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace XcTools.Models
{
    public class Category : INotifyPropertyChanged
    {
        private string _name;
        private string _path;
        private List<Category> _subCategories;
        private bool _isRoot;
        private bool _isExpanded;

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public string Path
        {
            get => _path;
            set { _path = value; OnPropertyChanged(); }
        }

        public List<Category> SubCategories
        {
            get => _subCategories;
            set { _subCategories = value; OnPropertyChanged(); }
        }

        public bool IsRoot
        {
            get => _isRoot;
            set { _isRoot = value; OnPropertyChanged(); }
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; OnPropertyChanged(); }
        }

        public Category()
        {
            _subCategories = new List<Category>();
            _isRoot = false;
            _isExpanded = false;
        }

        public Category(string name, string path, bool isRoot = false)
        {
            _name = name;
            _path = path;
            _subCategories = new List<Category>();
            _isRoot = isRoot;
            _isExpanded = false;
        }

        public void AddSubCategory(Category subCategory)
        {
            _subCategories.Add(subCategory);
            OnPropertyChanged(nameof(SubCategories));
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
