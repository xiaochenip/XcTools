using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using System;
using System.Collections.Generic;
using System.IO;
using IOPath = System.IO.Path;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using XcTools.Models;
using XcTools.Services;

namespace XcTools.Views
{
    /// <summary>
    /// LibraryManagerView.xaml 的交互逻辑
    /// </summary>
    public partial class LibraryManagerView : Window
    {
        private string currentCategoryPath;
        private bool isThumbnailLoading = false;
        private DateTime lastThumbnailLoadTime = DateTime.MinValue;
        private const int THUMBNAIL_LOAD_THROTTLE_MS = 100; // 节流时间，避免频繁调用
        private const int MAX_CONCURRENT_THUMBNAIL_TASKS = 3; // 最大并发缩略图任务数
        
        // 分页相关字段
        private List<FileItem> allFileItems = new List<FileItem>();
        private int currentPage = 1;
        private const int itemsPerPage = 20; // 每页显示20个
        
        // 缩略图加载队列
        private readonly Queue<FileItem> thumbnailQueue = new Queue<FileItem>();
        private readonly object queueLock = new object();

        public LibraryManagerView()
        {
            InitializeComponent();

            // 从插件包根目录加载窗口图标
            Icon = App.LoadWindowIcon();

            // 恢复窗口状态
            try
            {
                WindowStateService.RestoreWindowState("LibraryManagerView", this);
            }
            catch { }
            
            // 注册窗口关闭事件，保存窗口状态
            this.Closing += LibraryManagerView_Closing;
            
            // 使用Dispatcher异步初始化，避免阻塞UI线程
            this.Dispatcher.BeginInvoke(new Action(async () =>
            {
                await InitializeAsync();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        /// <summary>窗口句柄创建后再设一次图标，防止 AutoCAD 宿主覆盖</summary>
        protected override void OnSourceInitialized(System.EventArgs e)
        {
            base.OnSourceInitialized(e);
            Icon = App.LoadWindowIcon();
        }

        private async System.Threading.Tasks.Task InitializeAsync()
        {
            try
            {
                InitializeCategoryTree();
                LoadLibraryFiles();
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"窗口初始化错误: {ex.Message}");
                MessageBox.Show($"初始化错误: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        /// <summary>
        /// 窗口关闭事件，保存窗口状态
        /// </summary>
        private void LibraryManagerView_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            WindowStateService.SaveWindowState("LibraryManagerView", this);
        }

        /// <summary>
        /// 初始化分类树
        /// </summary>
        private void InitializeCategoryTree()
        {
            try
            {
                // 使用LibraryManagerService获取分类，减少代码冗余
                List<Category> categories = LibraryManagerService.GetCategories();
                
                // 设置分类树数据源
                categoryTreeView.ItemsSource = categories;

                // 添加选择事件
                categoryTreeView.SelectedItemChanged += CategoryTreeView_SelectedItemChanged;

                // 默认展开并选中第一个分类
                if (categoryTreeView.Items.Count > 0)
                {
                    Category firstCategory = categories[0];
                    
                    // 监听ItemContainerGenerator的状态变化，确保TreeViewItem已生成
                    if (categoryTreeView.ItemContainerGenerator.Status == System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated)
                    {
                        ExpandCategoryTree(firstCategory);
                    }
                    else
                    {
                        // 创建一个临时的事件处理程序，只执行一次后移除
                        System.EventHandler statusChangedHandler = null;
                        statusChangedHandler = (sender, e) =>
                        {
                            if (categoryTreeView.ItemContainerGenerator.Status == System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated)
                            {
                                // 移除事件监听，避免重复执行
                                categoryTreeView.ItemContainerGenerator.StatusChanged -= statusChangedHandler;
                                // 展开分类树
                                ExpandCategoryTree(firstCategory);
                            }
                        };
                        // 注册StatusChanged事件
                        categoryTreeView.ItemContainerGenerator.StatusChanged += statusChangedHandler;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"初始化分类树错误: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        /// <summary>
        /// 展开分类树
        /// </summary>
        /// <param name="firstCategory">第一个分类</param>
        private void ExpandCategoryTree(Category firstCategory)
        {
            TreeViewItem rootItem = categoryTreeView.ItemContainerGenerator.ContainerFromItem(firstCategory) as TreeViewItem;
            if (rootItem != null)
            {
                rootItem.IsExpanded = true;
                rootItem.IsSelected = true;
                currentCategoryPath = firstCategory.Path;
                
                // 递归展开所有分类
                ExpandAllCategories(rootItem);
            }
        }
        
        /// <summary>
        /// 递归展开所有分类
        /// </summary>
        /// <param name="parentItem">父分类项</param>
        private void ExpandAllCategories(TreeViewItem parentItem)
        {
            if (parentItem == null)
                return;
            
            parentItem.IsExpanded = true;
            
            // 获取数据对象
            Category category = parentItem.DataContext as Category;
            if (category != null)
            {
                category.IsExpanded = true;
            }
            
            // 遍历所有子项
            foreach (object child in parentItem.Items)
            {
                TreeViewItem childItem = parentItem.ItemContainerGenerator.ContainerFromItem(child) as TreeViewItem;
                if (childItem != null)
                {
                    ExpandAllCategories(childItem);
                }
            }
        }

        /// <summary>
        /// 加载图库文件
        /// </summary>
        private async void LoadLibraryFiles()
        {
            try
            {
                if (string.IsNullOrEmpty(currentCategoryPath))
                {
                    currentCategoryPath = App.LibraryPath;
                }
                
                txtCurrentPath.Text = currentCategoryPath;
                
                allFileItems = LibraryManagerService.GetLibraryFiles(currentCategoryPath, true);
                
                currentPage = 1;
                ShowCurrentPage();
                
                RegisterListBoxScrollEvent();
                
                await LoadCurrentPageThumbnails();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载图库文件错误: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        /// <summary>
        /// 加载当前页的缩略图
        /// </summary>
        private async Task LoadCurrentPageThumbnails()
        {
            try
            {
                var currentPageItems = fileListView.ItemsSource as List<FileItem>;
                if (currentPageItems != null && currentPageItems.Count > 0)
                {
                    foreach (var item in currentPageItems)
                    {
                        item.Thumbnail = await ThumbnailManagerService.GenerateThumbnailAsync(item.FullPath);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载缩略图错误: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 显示当前页的文件
        /// </summary>
        private void ShowCurrentPage()
        {
            // 计算总页数
            int totalPages = (int)Math.Ceiling((double)allFileItems.Count / itemsPerPage);
            
            // 确保页码有效
            currentPage = Math.Max(1, Math.Min(currentPage, totalPages));
            
            // 显示当前页的文件
            List<FileItem> currentPageItems = new List<FileItem>();
            if (allFileItems.Count > 0)
            {
                // 计算当前页的文件范围
                int startIndex = (currentPage - 1) * itemsPerPage;
                int endIndex = Math.Min(startIndex + itemsPerPage, allFileItems.Count);
                
                // 获取当前页的文件
                currentPageItems = allFileItems.GetRange(startIndex, endIndex - startIndex);
                
                // 初始化缩略图为null，准备延迟加载
                foreach (var item in currentPageItems)
                {
                    item.Thumbnail = null;
                }
            }
            
            fileListView.ItemsSource = currentPageItems;
            
            UpdatePageButtonStates();
        }
        
        private void UpdatePageButtonStates()
        {
            int totalPages = (int)Math.Ceiling((double)allFileItems.Count / itemsPerPage);
            
            btnFirstPage.IsEnabled = allFileItems.Count > itemsPerPage && currentPage > 1;
            btnPrevPage.IsEnabled = allFileItems.Count > itemsPerPage && currentPage > 1;
            btnNextPage.IsEnabled = allFileItems.Count > itemsPerPage && currentPage < totalPages;
            btnLastPage.IsEnabled = allFileItems.Count > itemsPerPage && currentPage < totalPages;
        }
        
        /// <summary>
        /// 首页按钮点击事件上一页按钮点击事件
        /// </summary>
        private void PrevPageButton_Click(object sender, RoutedEventArgs e)
        {
            int totalPages = (int)Math.Ceiling((double)allFileItems.Count / itemsPerPage);
            if (currentPage > 1)
            {
                currentPage--;
                ShowCurrentPage();
                _ = LoadCurrentPageThumbnails();
            }
        }
        
        /// <summary>
        /// 下一页按钮点击事件
        /// </summary>
        private void NextPageButton_Click(object sender, RoutedEventArgs e)
        {
            int totalPages = (int)Math.Ceiling((double)allFileItems.Count / itemsPerPage);
            if (currentPage < totalPages)
            {
                currentPage++;
                ShowCurrentPage();
                _ = LoadCurrentPageThumbnails();
            }
        }
        
        /// <summary>
        /// 注册ListBox的滚动事件，实现缩略图的延迟加载
        /// </summary>
        private void RegisterListBoxScrollEvent()
        {
            // 获取ListBox的ScrollViewer
            ScrollViewer scrollViewer = FindVisualChild<ScrollViewer>(fileListView);
            if (scrollViewer != null)
            {
                // 注册滚动事件
                scrollViewer.ScrollChanged += ScrollViewer_ScrollChanged;
            }
            
            // 初始加载可见区域的缩略图
            LoadVisibleItemsThumbnails();
        }
        
        /// <summary>
        /// 查找Visual的子元素
        /// </summary>
        private T FindVisualChild<T>(DependencyObject obj) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(obj, i);
                if (child != null && child is T)
                {
                    return (T)child;
                }
                else
                {
                    T childOfChild = FindVisualChild<T>(child);
                    if (childOfChild != null)
                    {
                        return childOfChild;
                    }
                }
            }
            return null;
        }
        
        /// <summary>
        /// 滚动事件处理
        /// </summary>
        private void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // 当滚动时，加载可见区域的缩略图，添加节流
            var now = DateTime.Now;
            if (now - lastThumbnailLoadTime > TimeSpan.FromMilliseconds(THUMBNAIL_LOAD_THROTTLE_MS))
            {
                lastThumbnailLoadTime = now;
                LoadVisibleItemsThumbnails();
            }
        }
        
        /// <summary>
        /// 加载可见区域的缩略图（异步）
        /// </summary>
        private async void LoadVisibleItemsThumbnails()
        {
            // 避免重入
            if (isThumbnailLoading)
            {
                return;
            }
            
            try
            {
                isThumbnailLoading = true;
                
                // 获取可见的ListBoxItem
                List<FileItem> visibleItems = new List<FileItem>();
                for (int i = 0; i < fileListView.Items.Count; i++)
                {
                    ListViewItem item = fileListView.ItemContainerGenerator.ContainerFromIndex(i) as ListViewItem;
                    if (item != null && IsElementVisible(item))
                    {
                        FileItem fileItem = fileListView.Items[i] as FileItem;
                        if (fileItem != null && fileItem.Thumbnail == null)
                        {
                            visibleItems.Add(fileItem);
                        }
                    }
                }
                
                // 异步处理可见项，每次只处理少量可见项，避免过多并发
                await Task.Run(async () =>
                {
                    try
                    {
                        // 只处理前10个可见项，避免过多并发任务
                        foreach (var fileItem in visibleItems.Take(10))
                        {
                            var thumbnail = await GetFileThumbnailAsync(fileItem.FullPath);
                            // 在UI线程更新缩略图
                            this.Dispatcher.Invoke(() =>
                            {
                                fileItem.Thumbnail = thumbnail;
                            });
                            
                            // 短暂延迟，避免资源占用过高
                            await Task.Delay(50);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"异步加载缩略图错误: {ex.Message}");
                    }
                    finally
                    {
                        // 重置加载状态
                        this.Dispatcher.Invoke(() =>
                        {
                            isThumbnailLoading = false;
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载可见区域缩略图错误: {ex.Message}");
                isThumbnailLoading = false;
            }
        }
        
        /// <summary>
        /// 检查元素是否可见
        /// </summary>
        private bool IsElementVisible(UIElement element)
        {
            if (!element.IsVisible)
            {
                return false;
            }
            
            Rect bounds = VisualTreeHelper.GetDescendantBounds(element);
            GeneralTransform transform = element.TransformToAncestor(fileListView);
            Rect rectangle = transform.TransformBounds(bounds);
            Rect listBoxRect = new Rect(0, 0, fileListView.ActualWidth, fileListView.ActualHeight);
            return listBoxRect.IntersectsWith(rectangle);
        }

        /// <summary>
        /// 获取文件缩略图（异步）
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <returns></returns>
        private async Task<BitmapImage> GetFileThumbnailAsync(string filePath)
        {
            try
            {
                // 使用ThumbnailManagerService异步生成缩略图
                return await ThumbnailManagerService.GenerateThumbnailAsync(filePath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"生成缩略图错误: {ex.Message}");
                // 如果无法获取缩略图，返回默认的CAD图标
                return GetDefaultCadThumbnail();
            }
        }
        
        /// <summary>
        /// 获取文件缩略图（同步方法，保持向后兼容）
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <returns></returns>
        private BitmapImage GetFileThumbnail(string filePath)
        {
            try
            {
                // 使用ThumbnailManagerService生成缩略图
                return ThumbnailManagerService.GenerateThumbnail(filePath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"生成缩略图错误: {ex.Message}");
                // 如果无法获取缩略图，返回默认的CAD图标
                return GetDefaultCadThumbnail();
            }
        }

        /// <summary>
        /// 获取默认的CAD图标
        /// </summary>
        /// <returns></returns>
        private BitmapImage GetDefaultCadThumbnail()
        {
            try
            {
                // 使用ThumbnailManagerService获取默认缩略图
                return ThumbnailManagerService.GetDefaultThumbnail();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"获取默认缩略图错误: {ex.Message}");
                // 如果获取默认缩略图失败，返回空
                return null;
            }
        }

        /// <summary>
        /// 分类选择变化事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CategoryTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is Category selectedCategory)
            {
                currentCategoryPath = selectedCategory.Path;
                // 更新全局当前选定分类路径
                App.CurrentSelectedCategoryPath = currentCategoryPath;
                LoadLibraryFiles();
            }
        }

        /// <summary>
        /// 分类双击事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Category_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // 双击分类时，展开或折叠子分类
            TreeViewItem item = FindTreeViewItem(sender as DependencyObject);
            if (item != null)
            {
                item.IsExpanded = !item.IsExpanded;
            }
        }

        /// <summary>
        /// 查找TreeViewItem
        /// </summary>
        /// <param name="depObj"></param>
        /// <returns></returns>
        private TreeViewItem FindTreeViewItem(DependencyObject depObj)
        {
            while (depObj != null && !(depObj is TreeViewItem))
            {
                depObj = VisualTreeHelper.GetParent(depObj);
            }
            return depObj as TreeViewItem;
        }

        /// <summary>
        /// 添加分类按钮点击事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void AddCategoryButton_Click(object sender, RoutedEventArgs e)
        {
            string categoryName = Microsoft.VisualBasic.Interaction.InputBox("请输入分类名称：", "添加分类");
            if (!string.IsNullOrWhiteSpace(categoryName))
            {
                try
                {
                    // 确保分类名称符合文件系统命名规则
                    if (categoryName.IndexOfAny(IOPath.GetInvalidFileNameChars()) >= 0)
                    {
                        MessageBox.Show("分类名称包含无效字符！", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    
                    // 检查分类是否已存在
                    string newCategoryPath = IOPath.Combine(currentCategoryPath ?? App.LibraryPath, categoryName);
                    if (Directory.Exists(newCategoryPath))
                    {
                        MessageBox.Show("分类已存在！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    
                    Directory.CreateDirectory(newCategoryPath);
                    InitializeCategoryTree();
                    MessageBox.Show("分类添加成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"添加分类错误: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            // 显示选择对话框，让用户选择入库方式
            string choice = Microsoft.VisualBasic.Interaction.InputBox("请选择入库方式：\n1 - 选择文件夹内的DWG文件\n2 - 从当前CAD图形选择", "选图入库", "1");
            
            if (choice == "1")
            {
                // 方式一：选择文件夹内的DWG文件
                AddFromFile();
            }
            else if (choice == "2")
            {
                // 方式二：从当前CAD图形选择
                AddFromCAD();
            }
            else
            {
                MessageBox.Show("无效的选择！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        
        /// <summary>
        /// 从文件选择DWG添加到图库
        /// </summary>
        private void AddFromFile()
        {
            // 选择目标分类
            string selectedCategoryPath = SelectCategory();
            if (string.IsNullOrEmpty(selectedCategoryPath))
                return;
            
            // 创建 OpenFileDialog
            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog();
            openFileDialog.Filter = "DWG文件|*.dwg|所有文件|*.*";
            openFileDialog.Multiselect = true; // 允许多选
            
            if (openFileDialog.ShowDialog() == true)
            {
                foreach (string sourceFile in openFileDialog.FileNames)
                {
                    try
                    {
                        // 使用用户选择的分类路径作为目标文件夹
                        bool success = LibraryManagerService.AddFileToLibrary(sourceFile, false, selectedCategoryPath);
                        if (!success)
                        {
                            // 检查是同名文件还是重复内容文件
                            string destFilePath = System.IO.Path.Combine(selectedCategoryPath, System.IO.Path.GetFileName(sourceFile));
                            if (File.Exists(destFilePath))
                            {
                                // 同名文件，询问是否覆盖
                                MessageBoxResult result = MessageBox.Show("文件已存在，是否覆盖？", "提示", MessageBoxButton.YesNo, MessageBoxImage.Question);
                                if (result == MessageBoxResult.Yes)
                                {
                                    success = LibraryManagerService.AddFileToLibrary(sourceFile, true, selectedCategoryPath);
                                }
                            }
                            else if (LibraryManagerService.IsDuplicateFile(sourceFile, selectedCategoryPath))
                            {
                                // 重复内容文件，提示用户
                                MessageBoxResult result = MessageBox.Show("库中已存在相同内容的文件，是否继续添加？", "提示", MessageBoxButton.YesNo, MessageBoxImage.Question);
                                if (result == MessageBoxResult.Yes)
                                {
                                    // 跳过重复文件检查，强制添加
                                    success = LibraryManagerService.AddFileToLibrary(sourceFile, false, selectedCategoryPath, false);
                                }
                            }
                        }

                        if (success)
                        {
                            LoadLibraryFiles();
                            MessageBox.Show("图形添加成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"添加图形错误: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }
        
        /// <summary>
        /// 从当前CAD图形选择添加到图库
        /// </summary>
        private void AddFromCAD()
        {
            // 选择目标分类
            string selectedCategoryPath = SelectCategory();
            if (string.IsNullOrEmpty(selectedCategoryPath))
                return;
            
            // 保存目标分类路径到全局变量
            App.AddToLibraryCategoryPath = selectedCategoryPath;
            
            // 关闭窗口，回到CAD界面选择图形
            this.Close();
            
            // 执行命令让用户选择图形并输入名称
            AcApp.DocumentManager.MdiActiveDocument.SendStringToExecute($"XCCAD_ADDFROMCAD ", true, false, false);
        }
        
        /// <summary>
        /// 选择目标分类
        /// </summary>
        /// <returns>分类路径，取消返回null</returns>
        private string SelectCategory()
        {
            CategorySelectDialog dialog = new CategorySelectDialog();
            bool? result = dialog.ShowDialog();
            
            if (result == true)
            {
                return dialog.SelectedCategoryPath;
            }
            
            return null;
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (fileListView.SelectedItem == null)
            {
                MessageBox.Show("请选择要删除的图形！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            MessageBoxResult result = MessageBox.Show("确定要删除选中的图形吗？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                Models.FileItem selectedItem = fileListView.SelectedItem as Models.FileItem;
                if (selectedItem != null)
                {
                    LibraryManagerService.DeleteFileFromLibrary(selectedItem.FullPath);
                    LoadLibraryFiles();
                    MessageBox.Show("图形删除成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"删除图形错误: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void InsertButton_Click(object sender, RoutedEventArgs e)
        {
            InsertSelectedFile();
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            // 清除缓存并重新加载
            ThumbnailManagerService.ClearCache();
            LoadLibraryFiles();
        }

        private void InsertMenuItem_Click(object sender, RoutedEventArgs e)
        {
            InsertSelectedFile();
        }

        private void CopyMenuItem_Click(object sender, RoutedEventArgs e)
        {
            CopyOrMoveFile(false);
        }

        private void MoveMenuItem_Click(object sender, RoutedEventArgs e)
        {
            CopyOrMoveFile(true);
        }

        private void RenameMenuItem_Click(object sender, RoutedEventArgs e)
        {
            RenameFile();
        }

        private void DeleteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            DeleteButton_Click(sender, e);
        }

        /// <summary>
        /// 复制或移动文件
        /// </summary>
        /// <param name="isMove">是否为移动操作</param>
        private void CopyOrMoveFile(bool isMove)
        {
            if (fileListView.SelectedItem == null)
            {
                MessageBox.Show($"请选择要{(isMove ? "移动" : "复制")}的图形！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            FileItem selectedFile = fileListView.SelectedItem as FileItem;
            if (selectedFile == null)
            {
                MessageBox.Show("无效的文件选择！", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                // 获取所有分类的名称
                var allCategories = LibraryManagerService.GetCategories();
                var categoryNames = new List<string>();

                // 递归提取所有分类名称
                void ExtractCategoryNames(Category category)
                {
                    if (!string.IsNullOrEmpty(category.Name) && category.Name != "全部图形")
                    {
                        categoryNames.Add(category.Name);
                    }
                    foreach (var subCategory in category.SubCategories)
                    {
                        ExtractCategoryNames(subCategory);
                    }
                }

                foreach (var category in allCategories)
                {
                    ExtractCategoryNames(category);
                }

                // 显示分类选择对话框
                string selectedCategory = Microsoft.VisualBasic.Interaction.InputBox(
                    $"请选择目标分类：\n可用分类：{string.Join(", ", categoryNames)}",
                    $"{(isMove ? "移动" : "复制")}文件",
                    "通用");

                if (!string.IsNullOrWhiteSpace(selectedCategory))
                {
                    // 在所有分类中查找匹配的分类路径
                    string destCategoryPath = FindCategoryPath(selectedCategory, allCategories);
                    if (string.IsNullOrEmpty(destCategoryPath))
                    {
                        MessageBox.Show("未找到指定的分类！", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    string destFile = IOPath.Combine(destCategoryPath, selectedFile.Name);

                    if (isMove)
                    {
                        // 移动文件（net48 无三参 File.Move，用 Copy+Delete 等价实现）
#if NET48
                        System.IO.File.Copy(selectedFile.FullPath, destFile, true);
                        System.IO.File.Delete(selectedFile.FullPath);
#else
                        System.IO.File.Move(selectedFile.FullPath, destFile, true);
#endif
                        MessageBox.Show("文件移动成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                        // 移动后刷新当前分类的文件列表
                        LoadLibraryFiles();
                    }
                    else
                    {
                        // 复制文件
                        System.IO.File.Copy(selectedFile.FullPath, destFile, true);
                        MessageBox.Show("文件复制成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"{(isMove ? "移动" : "复制")}文件错误: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 重命名文件
        /// </summary>
        private void RenameFile()
        {
            if (fileListView.SelectedItem == null)
            {
                MessageBox.Show("请选择要重命名的图形！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            FileItem selectedFile = fileListView.SelectedItem as FileItem;
            if (selectedFile == null)
            {
                MessageBox.Show("无效的文件选择！", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                // 获取当前文件名（不带扩展名）
                string fileNameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(selectedFile.Name);
                string fileExt = System.IO.Path.GetExtension(selectedFile.Name);

                // 打开重命名对话框
                string newFileName = Microsoft.VisualBasic.Interaction.InputBox("请输入新的文件名：", "重命名文件", fileNameWithoutExt);
                if (string.IsNullOrWhiteSpace(newFileName))
                {
                    return;
                }

                // 验证文件名是否有效
                if (newFileName.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
                {
                    MessageBox.Show("文件名包含无效字符！", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 构建新文件路径
                string newFileNameWithExt = newFileName + fileExt;
                string destFile = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(selectedFile.FullPath), newFileNameWithExt);

                // 检查新文件名是否已存在
                if (System.IO.File.Exists(destFile))
                {
                    MessageBoxResult overwriteResult = MessageBox.Show($"文件 {newFileNameWithExt} 已存在，是否覆盖？", "提示", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (overwriteResult != MessageBoxResult.Yes)
                    {
                        return;
                    }
                }

                // 执行重命名操作（net48 无三参 File.Move，用 Copy+Delete 等价实现）
#if NET48
                System.IO.File.Copy(selectedFile.FullPath, destFile, true);
                System.IO.File.Delete(selectedFile.FullPath);
#else
                System.IO.File.Move(selectedFile.FullPath, destFile, true);
#endif
                MessageBox.Show("文件重命名成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);

                // 刷新当前分类的文件列表
                LoadLibraryFiles();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"重命名文件错误: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 在分类树中查找指定名称的分类路径
        /// </summary>
        /// <param name="categoryName">分类名称</param>
        /// <param name="categories">分类列表</param>
        /// <returns>分类路径，未找到返回null</returns>
        private string FindCategoryPath(string categoryName, List<Category> categories)
        {
            foreach (var category in categories)
            {
                if (category.Name == categoryName)
                {
                    return category.Path;
                }
                // 递归查找子分类
                var foundPath = FindCategoryPath(categoryName, category.SubCategories);
                if (!string.IsNullOrEmpty(foundPath))
                {
                    return foundPath;
                }
            }
            return null;
        }

        /// <summary>
        /// 缩略图鼠标进入事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Thumbnail_MouseEnter(object sender, MouseEventArgs e)
        {
            // 缩略图鼠标悬停效果（可选实现）
            if (sender is System.Windows.Controls.Image image)
            {
                image.Opacity = 0.8;
            }
        }

        /// <summary>
        /// 缩略图鼠标离开事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Thumbnail_MouseLeave(object sender, MouseEventArgs e)
        {
            // 恢复缩略图透明度
            if (sender is System.Windows.Controls.Image image)
            {
                image.Opacity = 1.0;
            }
        }

        private void InsertSelectedFile()
        {
            if (fileListView.SelectedItem == null)
            {
                MessageBox.Show("请选择要插入的图形！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            InsertFile(fileListView.SelectedItem as Models.FileItem);
        }

        /// <summary>
        /// 文件列表鼠标双击事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FileListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (fileListView.SelectedItem != null)
            {
                InsertFile(fileListView.SelectedItem as Models.FileItem);
            }
        }

        private void FileListView_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 获取点击位置的ListBoxItem
            ListViewItem item = FindVisualParent<ListViewItem>((DependencyObject)e.OriginalSource);
            if (item != null)
            {
                fileListView.SelectedItem = item.DataContext;
            }
            
            // 如果有选中项，显示右键菜单
            if (fileListView.SelectedItem != null)
            {
                fileContextMenu.PlacementTarget = (UIElement)sender;
                fileContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
                fileContextMenu.IsOpen = true;
            }
        }
        
        private T FindVisualParent<T>(DependencyObject obj) where T : DependencyObject
        {
            while (obj != null && !(obj is T))
            {
                obj = VisualTreeHelper.GetParent(obj);
            }
            return obj as T;
        }
        
        private void ContextMenu_Insert_Click(object sender, RoutedEventArgs e)
        {
            if (fileListView.SelectedItem != null)
            {
                InsertFile(fileListView.SelectedItem as Models.FileItem);
            }
        }
        
        private void ContextMenu_Rename_Click(object sender, RoutedEventArgs e)
        {
            RenameFile();
        }
        
        private void ContextMenu_Delete_Click(object sender, RoutedEventArgs e)
        {
            DeleteButton_Click(sender, e);
        }

        /// <summary>
        /// 插入指定的文件
        /// </summary>
        /// <param name="fileItem">要插入的文件项</param>
        private void InsertFile(Models.FileItem fileItem)
        {
            if (fileItem == null)
            {
                MessageBox.Show("请选择要插入的图形！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                this.Close();
                // 将选中的文件路径保存到临时位置，以便插入命令使用
                App.CurrentSelectedFilePath = fileItem.FullPath;
                // 设置跳过文件选择（已选择文件），但仍然提示比例和旋转
                App.SkipScalePrompt = false;
                // 执行插入命令
                AcApp.DocumentManager.MdiActiveDocument.SendStringToExecute($"XCCAD_INSERTDWG ", true, false, false);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"插入图形错误: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 更换图库目录按钮点击事件
        /// </summary>
        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            using (var folderDialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                folderDialog.Description = "选择图库根目录（更换后不会自动创建默认分类）";
                folderDialog.SelectedPath = App.LibraryPath;
                
                if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    string newPath = folderDialog.SelectedPath;
                    
                    if (MessageBox.Show($"确定要将图库目录更换为：\n{newPath}\n\n更换后将使用该目录作为新的图库根目录，不会自动创建默认分类。", 
                        "更换图库目录", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        try
                        {
                            LibraryManagerService.SetLibraryPath(newPath);
                            App.SetLibraryPath(newPath);
                            
                            currentCategoryPath = newPath;
                            txtCurrentPath.Text = newPath;
                            
                            InitializeCategoryTree();
                            LoadLibraryFiles();
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"更换图库目录失败：{ex.Message}", 
                                "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 首页按钮点击事件
        /// </summary>
        private void FirstPageButton_Click(object sender, RoutedEventArgs e)
        {
            currentPage = 1;
            ShowCurrentPage();
            _ = LoadCurrentPageThumbnails();
        }

        /// <summary>
        /// 尾页按钮点击事件
        /// </summary>
        private void LastPageButton_Click(object sender, RoutedEventArgs e)
        {
            int totalPages = (int)Math.Ceiling((double)allFileItems.Count / itemsPerPage);
            currentPage = Math.Max(1, totalPages);
            ShowCurrentPage();
            _ = LoadCurrentPageThumbnails();
        }

        /// <summary>
        /// 帮助按钮点击事件
        /// </summary>
        private void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                HelpDialog dialog = new HelpDialog();
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开帮助对话框错误: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 关闭按钮点击事件
        /// </summary>
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}