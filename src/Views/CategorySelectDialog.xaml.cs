using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using XcTools.Models;
using XcTools.Services;

namespace XcTools.Views
{
    public partial class CategorySelectDialog : Window
    {
        public string SelectedCategoryPath { get; private set; }
        
        private string parentCategoryPath;
        
        public CategorySelectDialog(string parentPath = null)
        {
            InitializeComponent();

            // 从插件包根目录加载窗口图标
            Icon = App.LoadWindowIcon();

            parentCategoryPath = parentPath;
            LoadCategories();
        }

        /// <summary>窗口句柄创建后再设一次图标，防止 AutoCAD 宿主覆盖</summary>
        protected override void OnSourceInitialized(System.EventArgs e)
        {
            base.OnSourceInitialized(e);
            Icon = App.LoadWindowIcon();
        }
        
        private void LoadCategories()
        {
            List<Category> categories = LibraryManagerService.GetCategories();
            categoryTreeView.ItemsSource = categories;
            
            // 展开所有节点
            ExpandAllNodes();
        }
        
        private void ExpandAllNodes()
        {
            foreach (var item in categoryTreeView.Items)
            {
                TreeViewItem treeItem = categoryTreeView.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
                ExpandNode(treeItem);
            }
        }
        
        private void ExpandNode(TreeViewItem item)
        {
            if (item == null) return;
            
            item.IsExpanded = true;
            
            foreach (var child in item.Items)
            {
                TreeViewItem childItem = item.ItemContainerGenerator.ContainerFromItem(child) as TreeViewItem;
                ExpandNode(childItem);
            }
        }
        
        private void BtnOK_Click(object sender, RoutedEventArgs e)
        {
            Category selectedCategory = categoryTreeView.SelectedItem as Category;
            if (selectedCategory != null)
            {
                SelectedCategoryPath = selectedCategory.Path;
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show("请选择一个分类！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        
        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
        
        private void BtnNewCategory_Click(object sender, RoutedEventArgs e)
        {
            // 获取当前选中的父分类
            Category selectedCategory = categoryTreeView.SelectedItem as Category;
            string parentPath = selectedCategory?.Path ?? App.LibraryPath;
            
            string categoryName = Microsoft.VisualBasic.Interaction.InputBox("请输入新分类名称：", "新建分类");
            if (!string.IsNullOrWhiteSpace(categoryName))
            {
                // 验证分类名称
                if (categoryName.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
                {
                    MessageBox.Show("分类名称包含无效字符！", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                
                string newCategoryPath = System.IO.Path.Combine(parentPath, categoryName);
                if (System.IO.Directory.Exists(newCategoryPath))
                {
                    MessageBox.Show("分类已存在！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                // 创建新分类
                System.IO.Directory.CreateDirectory(newCategoryPath);
                
                // 重新加载分类树
                LoadCategories();
                
                // 选中新建的分类
                SelectCategoryByPath(newCategoryPath);
                
                MessageBox.Show("分类创建成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        
        private void SelectCategoryByPath(string path)
        {
            foreach (var item in categoryTreeView.Items)
            {
                TreeViewItem treeItem = categoryTreeView.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
                if (SelectCategoryRecursive(treeItem, path))
                    return;
            }
        }
        
        private bool SelectCategoryRecursive(TreeViewItem treeItem, string path)
        {
            if (treeItem == null) return false;
            
            Category category = treeItem.DataContext as Category;
            if (category != null && category.Path == path)
            {
                treeItem.IsSelected = true;
                return true;
            }
            
            foreach (var child in treeItem.Items)
            {
                TreeViewItem childItem = treeItem.ItemContainerGenerator.ContainerFromItem(child) as TreeViewItem;
                if (SelectCategoryRecursive(childItem, path))
                    return true;
            }
            
            return false;
        }
    }
}