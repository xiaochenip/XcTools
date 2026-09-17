using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using XcTools.Models;

namespace XcTools.Services
{
    /// <summary>
    /// 图库管理服务类
    /// </summary>
    public class LibraryManagerService
    {
        /// <summary>
        /// 图库文件夹路径常量
        /// </summary>
        private const string LIBRARY_FOLDER_NAME = "library";

        /// <summary>
        /// 获取默认图库文件夹路径
        /// </summary>
        public static string GetDefaultLibraryPath()
        {
            // 插件根目录（DLL 在 net8/net48 子目录时取父层），config.ini 与 library 都在根目录
            string assemblyDir = App.PluginDir;
            
            string configFile = Path.Combine(assemblyDir, "config.ini");
            if (File.Exists(configFile))
            {
                try
                {
                    string[] lines = File.ReadAllLines(configFile);
                    foreach (string line in lines)
                    {
                        if (line.StartsWith("LibraryPath=", StringComparison.OrdinalIgnoreCase))
                        {
                            string path = line.Substring("LibraryPath=".Length).Trim();
                            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                            {
                                return path;
                            }
                        }
                    }
                }
                catch { }
            }
            
            return Path.Combine(assemblyDir, LIBRARY_FOLDER_NAME);
        }

        /// <summary>
        /// 获取图库文件夹路径（支持自定义路径）
        /// </summary>
        public static string GetLibraryPath()
        {
            return SettingsService.GetLibraryPath();
        }

        /// <summary>
        /// 设置图库文件夹路径
        /// </summary>
        /// <param name="path">新的图库路径</param>
        public static void SetLibraryPath(string path)
        {
            SettingsService.SetLibraryPath(path);
        }

        /// <summary>
        /// 重置图库文件夹路径为默认值
        /// </summary>
        public static void ResetLibraryPath()
        {
            SettingsService.ResetLibraryPath();
        }

        /// <summary>
        /// 确保图库文件夹存在
        /// </summary>
        public static void EnsureLibraryFolderExists()
        {
            string libraryPath = GetLibraryPath();
            if (!Directory.Exists(libraryPath))
            {
                Directory.CreateDirectory(libraryPath);
            }
        }
        
        /// <summary>
        /// 确保默认分类存在
        /// </summary>
        public static void EnsureDefaultCategoriesExists()
        {
            try
            {
                string libraryPath = GetLibraryPath();
                EnsureLibraryFolderExists();
                
                // 默认分类列表
                string[] defaultCategories = { "建筑(ARCH)", "结构(STR)", "给排水(PL)", "电气(ELEC)", "暖通(HVAC)", "装饰(DECO)", "通用(GEN)" };

                foreach (string categoryName in defaultCategories)
                {
                    string categoryPath = Path.Combine(libraryPath, categoryName);
                    if (!Directory.Exists(categoryPath))
                    {
                        Directory.CreateDirectory(categoryPath);
                    }
                }
            }
            catch (Exception ex)
            {
                // 记录错误日志
                System.Diagnostics.Debug.WriteLine($"创建默认分类错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取图库中的DWG文件
        /// </summary>
        /// <param name="folderPath">文件夹路径，若为null则使用图库根目录</param>
        /// <param name="searchSubdirectories">是否搜索子目录</param>
        /// <returns></returns>
        public static List<FileItem> GetLibraryFiles(string folderPath = null, bool searchSubdirectories = false)
        {
            string libraryPath = string.IsNullOrEmpty(folderPath) ? GetLibraryPath() : folderPath;
            EnsureLibraryFolderExists();

            SearchOption searchOption = searchSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            string[] dwgFiles = Directory.GetFiles(libraryPath, "*.dwg", searchOption);
            
            // 使用LINQ查询优化文件信息获取
            return dwgFiles.Select(filePath =>
            {
                FileInfo fi = new FileInfo(filePath);
                
                // 计算分类信息
                string category = GetFileCategory(filePath, libraryPath);
                
                return new FileItem
                {
                    FullPath = filePath,
                    Name = fi.Name,
                    Size = fi.Length.ToString("N0") + " 字节",
                    LastWriteTime = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    Category = category,
                    // 缩略图将在视图层按需生成，避免提前加载导致的性能问题
                    Thumbnail = null
                };
            }).OrderBy(f => f.Name) // 按文件名排序
              .ToList();
        }
        
        /// <summary>
        /// 获取文件的分类信息
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <param name="libraryPath">图库根路径</param>
        /// <returns>分类名称</returns>
        private static string GetFileCategory(string filePath, string libraryPath)
        {
            // 获取文件所在目录
            string fileDirectory = Path.GetDirectoryName(filePath);
            
            // 计算相对于图库根目录的路径
            string relativePath = fileDirectory.Substring(libraryPath.Length);
            
            // 移除前导路径分隔符
            if (relativePath.StartsWith(Path.DirectorySeparatorChar.ToString()))
            {
                relativePath = relativePath.Substring(1);
            }
            
            // 获取第一个子目录作为分类
            if (!string.IsNullOrEmpty(relativePath))
            {
                string[] pathParts = relativePath.Split(Path.DirectorySeparatorChar);
                if (pathParts.Length > 0 && !string.IsNullOrEmpty(pathParts[0]))
                {
                    return pathParts[0];
                }
            }
            
            // 默认分类
            return "未分类";
        }

        /// <summary>
        /// 将DWG文件添加到图库
        /// </summary>
        /// <param name="sourceFilePath">源文件路径</param>
        /// <param name="overwrite">是否覆盖已存在的文件</param>
        /// <param name="targetFolderPath">目标文件夹路径，若为null则保存到图库根目录</param>
        /// <param name="checkDuplicate">是否检查重复文件</param>
        /// <returns>操作结果</returns>
        public static bool AddFileToLibrary(string sourceFilePath, bool overwrite = false, string targetFolderPath = null, bool checkDuplicate = true)
        {
            try
            {
                // 验证源文件存在且为DWG文件
                if (!File.Exists(sourceFilePath) || !sourceFilePath.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                string libraryPath = GetLibraryPath();
                EnsureLibraryFolderExists();

                // 如果未指定目标文件夹，则使用图库根目录
                string destDirectory = string.IsNullOrEmpty(targetFolderPath) ? libraryPath : targetFolderPath;
                EnsureDirectoryExists(destDirectory);
                
                string destFilePath = Path.Combine(destDirectory, Path.GetFileName(sourceFilePath));

                // 检查目标文件是否存在且不允许覆盖
                if (File.Exists(destFilePath) && !overwrite)
                {
                    return false;
                }
                
                // 检查是否存在重复文件（内容相同但文件名不同）
                if (checkDuplicate && IsDuplicateFile(sourceFilePath, destDirectory))
                {
                    return false;
                }

                // 复制文件，使用FileOptions优化复制操作
                File.Copy(sourceFilePath, destFilePath, overwrite);
                
                // 异步生成新添加文件的缩略图，避免阻塞主线程
                Task.Run(async () =>
                {
                    try
                    {
                        await ThumbnailManagerService.GenerateThumbnailAsync(destFilePath);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"异步生成缩略图错误: {ex.Message}");
                        // 异步生成失败不影响主流程
                    }
                });
                
                return true;
            }
            catch (Exception ex)
            {
                // 记录错误日志（可扩展）
                System.Diagnostics.Debug.WriteLine($"添加文件到图库错误: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 从图库中删除文件
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <returns>操作结果</returns>
        public static bool DeleteFileFromLibrary(string filePath)
        {
            try
            {
                // 验证文件存在且位于图库目录内
                if (File.Exists(filePath) && filePath.StartsWith(GetLibraryPath(), StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(filePath);
                    
                    // 不再直接删除缩略图，因为其他文件可能共享同一个缩略图
                    // 缩略图会在CleanupExpiredThumbnails方法中定期清理
                    // 清理过期的缩略图，确保没有对应的DWG文件的缩略图被删除
                    ThumbnailManagerService.CleanupExpiredThumbnails();
                    
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                // 记录错误日志（可扩展）
                System.Diagnostics.Debug.WriteLine($"从图库删除文件错误: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 确保目录存在
        /// </summary>
        /// <param name="directoryPath">目录路径</param>
        private static void EnsureDirectoryExists(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }
        }
        
        /// <summary>
        /// 计算文件的MD5哈希值
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <returns>文件的MD5哈希值</returns>
        public static string CalculateFileHash(string filePath)
        {
            using (System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create())
            {
                using (FileStream stream = File.OpenRead(filePath))
                {
                    byte[] hashBytes = md5.ComputeHash(stream);
                    return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
                }
            }
        }
        
        /// <summary>
        /// 检查是否存在重复文件
        /// </summary>
        /// <param name="sourceFilePath">源文件路径</param>
        /// <param name="targetFolderPath">目标文件夹路径</param>
        /// <returns>是否存在重复文件</returns>
        public static bool IsDuplicateFile(string sourceFilePath, string targetFolderPath)
        {
            try
            {
                // 计算源文件的哈希值
                string sourceHash = CalculateFileHash(sourceFilePath);
                string sourceFileName = Path.GetFileName(sourceFilePath);
                string sourceFileExt = Path.GetExtension(sourceFilePath);
                
                // 获取目标文件夹中的所有DWG文件
                string[] dwgFiles = Directory.GetFiles(targetFolderPath, "*.dwg", SearchOption.AllDirectories);
                
                foreach (string filePath in dwgFiles)
                {
                    // 跳过同名文件（已经在AddFileToLibrary中处理）
                    if (Path.GetFileName(filePath).Equals(sourceFileName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    
                    // 计算目标文件的哈希值并比较
                    string targetHash = CalculateFileHash(filePath);
                    if (sourceHash == targetHash)
                    {
                        return true;
                    }
                }
                
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"检测重复文件错误: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取图库中的所有分类
        /// </summary>
        /// <returns>分类列表</returns>
        public static List<Category> GetCategories()
        {
            string libraryPath = GetLibraryPath();
            EnsureLibraryFolderExists();
            
            List<Category> categories = new List<Category>();
            
            // 创建根分类
            Category rootCategory = new Category("全部图形", libraryPath, true);
            categories.Add(rootCategory);
            
            // 加载一级分类，将其作为根分类的子分类
            string[] subDirectories = Directory.GetDirectories(libraryPath);
            foreach (string dir in subDirectories)
            {
                string dirName = Path.GetFileName(dir);
                Category subCategory = new Category(dirName, dir);
                rootCategory.SubCategories.Add(subCategory); // 添加到根分类的子分类中
                
                // 加载子分类
                LoadSubCategories(subCategory);
            }
            
            return categories;
        }

        /// <summary>
        /// 递归加载子分类
        /// </summary>
        /// <param name="parentCategory">父分类</param>
        private static void LoadSubCategories(Category parentCategory)
        {
            string[] subDirectories = Directory.GetDirectories(parentCategory.Path);
            foreach (string dir in subDirectories)
            {
                string dirName = Path.GetFileName(dir);
                Category subCategory = new Category(dirName, dir);
                parentCategory.AddSubCategory(subCategory);
                
                LoadSubCategories(subCategory);
            }
        }
    }
}
