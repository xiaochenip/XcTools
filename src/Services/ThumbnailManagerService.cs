using System;
using System.IO;
using System.Drawing;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.ApplicationServices;
using System.Threading;
using System.Security.Cryptography;

namespace XcTools.Services
{
    /// <summary>
    /// 缩略图管理服务类
    /// </summary>
    public class ThumbnailManagerService
    {
        /// <summary>
        /// 缩略图文件夹名称
        /// </summary>
        private const string THUMBNAIL_FOLDER_NAME = "thumbnails";
        
        /// <summary>
        /// 缩略图宽度，可配置，默认128x128像素
        /// </summary>
        public static int THUMBNAIL_WIDTH = 128;
        
        /// <summary>
        /// 缩略图高度
        /// </summary>
        public static int THUMBNAIL_HEIGHT = 128;
        
        /// <summary>
        /// 生成缩略图的超时时间（毫秒）
        /// </summary>
        private const int GENERATION_TIMEOUT = 5000;
        
        /// <summary>
        /// 缩略图缓存字典，避免重复生成
        /// </summary>
        private static readonly Dictionary<string, BitmapImage> thumbnailCache = new Dictionary<string, BitmapImage>();
        
        /// <summary>
        /// 缓存锁，保证线程安全
        /// </summary>
        private static readonly object cacheLock = new object();
        
        /// <summary>
        /// 获取缩略图文件夹路径
        /// </summary>
        /// <returns>缩略图文件夹路径</returns>
        public static string GetThumbnailFolderPath()
        {
            // 将缩略图文件夹移到图库目录外，避免显示在分类树中
            string libraryPath = LibraryManagerService.GetLibraryPath();
            string parentPath = Path.GetDirectoryName(libraryPath);
            return Path.Combine(parentPath, THUMBNAIL_FOLDER_NAME);
        }
        
        /// <summary>
        /// 确保缩略图文件夹存在
        /// </summary>
        public static void EnsureThumbnailFolderExists()
        {
            string thumbnailPath = GetThumbnailFolderPath();
            if (!Directory.Exists(thumbnailPath))
            {
                Directory.CreateDirectory(thumbnailPath);
            }
        }
        
        /// <summary>
        /// 获取文件的缩略图路径，使用MD5哈希命名，确保相同内容的文件复用同一个缩略图
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <returns>缩略图路径</returns>
        public static string GetThumbnailPath(string filePath)
        {
            string fileHash = LibraryManagerService.CalculateFileHash(filePath);
            string thumbnailFolder = GetThumbnailFolderPath();
            return Path.Combine(thumbnailFolder, $"{fileHash}.png");
        }
        
        /// <summary>
        /// 从缓存获取缩略图
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <returns>缩略图，如果缓存中没有则返回null</returns>
        private static BitmapImage GetThumbnailFromCache(string filePath)
        {
            lock (cacheLock)
            {
                string key = filePath.ToLowerInvariant();
                if (thumbnailCache.ContainsKey(key))
                {
                    return thumbnailCache[key];
                }
                return null;
            }
        }
        
        /// <summary>
        /// 将缩略图添加到缓存
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <param name="thumbnail">缩略图</param>
        private static void AddThumbnailToCache(string filePath, BitmapImage thumbnail)
        {
            if (thumbnail == null) return;
            
            lock (cacheLock)
            {
                string key = filePath.ToLowerInvariant();
                thumbnailCache[key] = thumbnail;
                
                // 限制缓存大小，避免内存占用过大
                if (thumbnailCache.Count > 100)
                {
                    var firstKey = thumbnailCache.Keys.GetEnumerator();
                    if (firstKey.MoveNext())
                    {
                        thumbnailCache.Remove(firstKey.Current);
                    }
                }
            }
        }
        
        /// <summary>
        /// 生成DWG文件的缩略图，使用AutoCAD API生成高质量真实缩略图
        /// 优化逻辑：优先使用缓存和文件，避免重复生成
        /// </summary>
        /// <param name="dwgFilePath">DWG文件路径</param>
        /// <returns>缩略图的BitmapImage对象</returns>
        public static BitmapImage GenerateThumbnail(string dwgFilePath)
        {
            return GenerateThumbnailInternal(dwgFilePath, false);
        }
        
        /// <summary>
        /// 强制重新生成缩略图
        /// </summary>
        /// <param name="dwgFilePath">DWG文件路径</param>
        /// <returns>缩略图的BitmapImage对象</returns>
        public static BitmapImage RegenerateThumbnail(string dwgFilePath)
        {
            return GenerateThumbnailInternal(dwgFilePath, true);
        }
        
        /// <summary>
        /// 内部缩略图生成方法
        /// </summary>
        /// <param name="dwgFilePath">DWG文件路径</param>
        /// <param name="forceRegenerate">是否强制重新生成</param>
        /// <returns>缩略图的BitmapImage对象</returns>
        private static BitmapImage GenerateThumbnailInternal(string dwgFilePath, bool forceRegenerate)
        {
            try
            {
                if (!forceRegenerate)
                {
                    // 步骤1: 优先从内存缓存获取
                    BitmapImage cachedThumbnail = GetThumbnailFromCache(dwgFilePath);
                    if (cachedThumbnail != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"从内存缓存获取缩略图: {dwgFilePath}");
                        return cachedThumbnail;
                    }
                    
                    // 步骤2: 检查文件系统中的缩略图
                    EnsureThumbnailFolderExists();
                    string thumbnailPath = GetThumbnailPath(dwgFilePath);
                    
                    if (File.Exists(thumbnailPath))
                    {
                        // 检查缩略图是否比原图新
                        DateTime dwgLastWriteTime = File.GetLastWriteTime(dwgFilePath);
                        DateTime thumbnailLastWriteTime = File.GetLastWriteTime(thumbnailPath);
                        
                        if (thumbnailLastWriteTime >= dwgLastWriteTime)
                        {
                            // 缩略图已存在且有效，直接加载
                            BitmapImage fileThumbnail = LoadThumbnailFromFile(thumbnailPath);
                            if (fileThumbnail != null)
                            {
                                AddThumbnailToCache(dwgFilePath, fileThumbnail);
                                System.Diagnostics.Debug.WriteLine($"从文件加载缩略图: {dwgFilePath}");
                                return fileThumbnail;
                            }
                        }
                    }
                }
                
                // 步骤3: 需要生成新的缩略图
                // 验证DWG文件存在且可访问
                if (!File.Exists(dwgFilePath))
                {
                    System.Diagnostics.Debug.WriteLine($"DWG文件不存在: {dwgFilePath}");
                    BitmapImage defaultThumb = GetDefaultThumbnail();
                    AddThumbnailToCache(dwgFilePath, defaultThumb);
                    return defaultThumb;
                }
                
                System.Diagnostics.Debug.WriteLine($"生成新缩略图: {dwgFilePath}");
                
                // 尝试从DWG文件提取真实缩略图
                BitmapImage dwgThumbnail = TryExtractDwgThumbnail(dwgFilePath);
                if (dwgThumbnail != null)
                {
                    // 保存缩略图到文件
                    string thumbnailPath = GetThumbnailPath(dwgFilePath);
                    try
                    {
                        SaveBitmapToPngFile(dwgThumbnail, thumbnailPath);
                    }
                    catch (Exception saveEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"保存缩略图到文件错误: {saveEx.Message}");
                    }
                    
                    AddThumbnailToCache(dwgFilePath, dwgThumbnail);
                    return dwgThumbnail;
                }
                
                // 如果提取失败，生成默认缩略图
                Bitmap finalThumbnail = GenerateDefaultThumbnail(dwgFilePath);
                
                try
                {
                    // 保存缩略图到文件
                    string thumbnailPath = GetThumbnailPath(dwgFilePath);
                    try
                    {
                        finalThumbnail.Save(thumbnailPath, System.Drawing.Imaging.ImageFormat.Png);
                    }
                    catch (Exception saveEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"保存缩略图到文件错误: {saveEx.Message}");
                    }
                    
                    // 转换为BitmapImage
                    BitmapImage bitmapImage = new BitmapImage();
                    using (MemoryStream memoryStream = new MemoryStream())
                    {
                        finalThumbnail.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Png);
                        memoryStream.Position = 0;
                        bitmapImage.BeginInit();
                        bitmapImage.StreamSource = memoryStream;
                        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                        bitmapImage.EndInit();
                        bitmapImage.Freeze();
                    }
                    
                    // 释放资源
                    finalThumbnail.Dispose();
                    
                    // 验证生成的缩略图是否有效
                    if (bitmapImage == null || bitmapImage.Width == 0 || bitmapImage.Height == 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"生成的缩略图无效: {dwgFilePath}");
                        BitmapImage defaultThumb = GetDefaultThumbnail();
                        AddThumbnailToCache(dwgFilePath, defaultThumb);
                        return defaultThumb;
                    }
                    
                    // 添加到缓存
                    AddThumbnailToCache(dwgFilePath, bitmapImage);
                    return bitmapImage;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"处理缩略图错误: {ex.Message}");
                    if (finalThumbnail != null)
                    {
                        finalThumbnail.Dispose();
                    }
                    BitmapImage defaultThumb = GetDefaultThumbnail();
                    AddThumbnailToCache(dwgFilePath, defaultThumb);
                    return defaultThumb;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"生成缩略图总错误: {ex.Message}");
                BitmapImage defaultThumb = GetDefaultThumbnail();
                AddThumbnailToCache(dwgFilePath, defaultThumb);
                return defaultThumb;
            }
        }
        
        /// <summary>
        /// 异步生成缩略图，返回Task
        /// </summary>
        /// <param name="dwgFilePath">DWG文件路径</param>
        /// <returns>缩略图的BitmapImage对象</returns>
        public static async Task<BitmapImage> GenerateThumbnailAsync(string dwgFilePath)
        {
            return await Task.Run(() => GenerateThumbnail(dwgFilePath));
        }
        
        /// <summary>
        /// 动态生成DWG缩略图（显示文件信息和视觉效果）
        /// </summary>
        /// <param name="dwgFilePath">DWG文件路径</param>
        /// <returns>动态生成的缩略图</returns>
        private static Bitmap GenerateDefaultThumbnail(string dwgFilePath)
        {
            System.Drawing.Bitmap thumbnailBitmap = new System.Drawing.Bitmap(THUMBNAIL_WIDTH, THUMBNAIL_HEIGHT);
            
            using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(thumbnailBitmap))
            {
                try
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    
                    // 根据文件名生成唯一颜色方案
                    System.Drawing.Color accentColor = GenerateAccentColor(dwgFilePath);
                    System.Drawing.Color lightColor = LightenColor(accentColor, 0.85f);
                    
                    // 绘制渐变背景
                    using (System.Drawing.Drawing2D.LinearGradientBrush bgBrush = 
                        new System.Drawing.Drawing2D.LinearGradientBrush(
                            new System.Drawing.Point(0, 0), 
                            new System.Drawing.Point(THUMBNAIL_WIDTH, THUMBNAIL_HEIGHT),
                            lightColor, 
                            System.Drawing.Color.White))
                    {
                        g.FillRectangle(bgBrush, 0, 0, THUMBNAIL_WIDTH, THUMBNAIL_HEIGHT);
                    }
                    
                    // 绘制装饰性几何图形
                    DrawDecorativeElements(g, accentColor);
                    
                    // 绘制DWG标识区域
                    int iconSize = 40;
                    int iconX = (THUMBNAIL_WIDTH - iconSize) / 2;
                    int iconY = 15;
                    
                    // 绘制DWG图标背景
                    using (System.Drawing.SolidBrush iconBgBrush = new System.Drawing.SolidBrush(accentColor))
                    {
                        g.FillRectangle(iconBgBrush, iconX, iconY, iconSize, iconSize);
                    }
                    
                    // 绘制DWG图标边框
                    using (System.Drawing.Pen iconBorderPen = new System.Drawing.Pen(System.Drawing.Color.White, 2))
                    {
                        g.DrawRectangle(iconBorderPen, iconX + 2, iconY + 2, iconSize - 4, iconSize - 4);
                    }
                    
                    // 绘制DWG文字
                    using (System.Drawing.Font dwgFont = new System.Drawing.Font("Arial", 12, System.Drawing.FontStyle.Bold))
                    using (System.Drawing.SolidBrush textBrush = new System.Drawing.SolidBrush(System.Drawing.Color.White))
                    {
                        System.Drawing.SizeF textSize = g.MeasureString("DWG", dwgFont);
                        float textX = iconX + (iconSize - textSize.Width) / 2;
                        float textY = iconY + (iconSize - textSize.Height) / 2;
                        g.DrawString("DWG", dwgFont, textBrush, textX, textY);
                    }
                    
                    // 绘制文件名
                    string fileName = System.IO.Path.GetFileNameWithoutExtension(dwgFilePath);
                    if (fileName.Length > 16)
                    {
                        fileName = fileName.Substring(0, 13) + "...";
                    }
                    
                    using (System.Drawing.Font nameFont = new System.Drawing.Font("Arial", 9))
                    using (System.Drawing.SolidBrush nameBrush = new System.Drawing.SolidBrush(System.Drawing.Color.Black))
                    {
                        System.Drawing.SizeF nameSize = g.MeasureString(fileName, nameFont);
                        float nameX = (THUMBNAIL_WIDTH - nameSize.Width) / 2;
                        float nameY = iconY + iconSize + 8;
                        g.DrawString(fileName, nameFont, nameBrush, nameX, nameY);
                    }
                    
                    // 绘制文件大小信息
                    try
                    {
                        FileInfo fileInfo = new FileInfo(dwgFilePath);
                        string fileSize = FormatFileSize(fileInfo.Length);
                        
                        using (System.Drawing.Font infoFont = new System.Drawing.Font("Arial", 7))
                        using (System.Drawing.SolidBrush infoBrush = new System.Drawing.SolidBrush(System.Drawing.Color.Gray))
                        {
                            System.Drawing.SizeF infoSize = g.MeasureString(fileSize, infoFont);
                            float infoX = (THUMBNAIL_WIDTH - infoSize.Width) / 2;
                            float infoY = iconY + iconSize + 22;
                            g.DrawString(fileSize, infoFont, infoBrush, infoX, infoY);
                        }
                    }
                    catch { }
                    
                    // 绘制底部装饰线
                    using (System.Drawing.Pen linePen = new System.Drawing.Pen(accentColor, 2))
                    {
                        g.DrawLine(linePen, 5, THUMBNAIL_HEIGHT - 3, THUMBNAIL_WIDTH - 5, THUMBNAIL_HEIGHT - 3);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"绘制默认缩略图错误: {ex.Message}");
                    thumbnailBitmap.Dispose();
                    return null;
                }
            }
            
            return thumbnailBitmap;
        }
        
        /// <summary>
        /// 根据文件名生成唯一的强调色
        /// </summary>
        private static System.Drawing.Color GenerateAccentColor(string fileName)
        {
            int hash = fileName.GetHashCode();
            float hue = (hash % 360) / 360f;
            return HslToRgb(hue, 0.6f, 0.55f);
        }
        
        /// <summary>
        /// HSL转RGB颜色
        /// </summary>
        private static System.Drawing.Color HslToRgb(float h, float s, float l)
        {
            float r, g, b;
            
            if (s == 0)
            {
                r = g = b = l;
            }
            else
            {
                float hue2rgb(float p, float q, float t)
                {
                    if (t < 0) t += 1;
                    if (t > 1) t -= 1;
                    if (t < 1f / 6) return p + (q - p) * 6 * t;
                    if (t < 1f / 2) return q;
                    if (t < 2f / 3) return p + (q - p) * (2f / 3 - t) * 6;
                    return p;
                }
                
                float q = l < 0.5f ? l * (1 + s) : l + s - l * s;
                float p = 2 * l - q;
                r = hue2rgb(p, q, h + 1f / 3);
                g = hue2rgb(p, q, h);
                b = hue2rgb(p, q, h - 1f / 3);
            }
            
            return System.Drawing.Color.FromArgb(
                (int)(r * 255), 
                (int)(g * 255), 
                (int)(b * 255));
        }
        
        /// <summary>
        /// 变亮颜色
        /// </summary>
        private static System.Drawing.Color LightenColor(System.Drawing.Color color, float factor)
        {
            int r = (int)(color.R * factor + 255 * (1 - factor));
            int g = (int)(color.G * factor + 255 * (1 - factor));
            int b = (int)(color.B * factor + 255 * (1 - factor));
            return System.Drawing.Color.FromArgb(r, g, b);
        }
        
        /// <summary>
        /// 绘制装饰性元素
        /// </summary>
        private static void DrawDecorativeElements(System.Drawing.Graphics g, System.Drawing.Color accentColor)
        {
            using (System.Drawing.Pen dotPen = new System.Drawing.Pen(accentColor, 3))
            {
                dotPen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dot;
                g.DrawLine(dotPen, 10, 10, THUMBNAIL_WIDTH - 10, 10);
                g.DrawLine(dotPen, 10, THUMBNAIL_HEIGHT - 20, THUMBNAIL_WIDTH - 10, THUMBNAIL_HEIGHT - 20);
            }
            
            // 绘制角落装饰点
            using (System.Drawing.SolidBrush dotBrush = new System.Drawing.SolidBrush(accentColor))
            {
                g.FillEllipse(dotBrush, 8, 8, 4, 4);
                g.FillEllipse(dotBrush, THUMBNAIL_WIDTH - 12, 8, 4, 4);
                g.FillEllipse(dotBrush, 8, THUMBNAIL_HEIGHT - 22, 4, 4);
                g.FillEllipse(dotBrush, THUMBNAIL_WIDTH - 12, THUMBNAIL_HEIGHT - 22, 4, 4);
            }
        }
        
        /// <summary>
        /// 格式化文件大小
        /// </summary>
        private static string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{(bytes / 1024.0):F1} KB";
            return $"{(bytes / (1024.0 * 1024)):F1} MB";
        }
        
        /// <summary>
        /// 从文件加载缩略图
        /// </summary>
        /// <param name="thumbnailPath">缩略图文件路径</param>
        /// <returns>缩略图的BitmapImage对象</returns>
        public static BitmapImage LoadThumbnailFromFile(string thumbnailPath)
        {
            try
            {
                if (File.Exists(thumbnailPath))
                {
                    // 验证文件大小，确保不是空文件
                    FileInfo fileInfo = new FileInfo(thumbnailPath);
                    if (fileInfo.Length == 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"缩略图文件为空: {thumbnailPath}");
                        return GetDefaultThumbnail();
                    }
                    
                    BitmapImage bitmapImage = new BitmapImage();
                    using (FileStream fileStream = new FileStream(thumbnailPath, FileMode.Open, FileAccess.Read))
                    {
                        // 尝试加载缩略图
                        bitmapImage.BeginInit();
                        bitmapImage.StreamSource = fileStream;
                        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                        bitmapImage.EndInit();
                        bitmapImage.Freeze();
                        
                        // 验证加载是否成功
                        if (bitmapImage.IsDownloading || bitmapImage.Width == 0 || bitmapImage.Height == 0)
                        {
                            System.Diagnostics.Debug.WriteLine($"缩略图加载失败: {thumbnailPath}");
                            return GetDefaultThumbnail();
                        }
                        
                        return bitmapImage;
                    }
                }
                System.Diagnostics.Debug.WriteLine($"缩略图文件不存在: {thumbnailPath}");
                return GetDefaultThumbnail();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载缩略图错误: {ex.Message}，路径: {thumbnailPath}");
                return GetDefaultThumbnail();
            }
        }
        
        /// <summary>
        /// 获取默认缩略图
        /// </summary>
        /// <returns>默认缩略图</returns>
        public static BitmapImage GetDefaultThumbnail()
        {
            try
            {
                // 创建一个简单的默认CAD图标
                Bitmap bmp = new Bitmap(THUMBNAIL_WIDTH, THUMBNAIL_HEIGHT);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    // 设置高质量渲染
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    
                    // 填充背景
                    g.FillRectangle(System.Drawing.Brushes.LightGray, 0, 0, THUMBNAIL_WIDTH, THUMBNAIL_HEIGHT);
                    
                    // 绘制CAD图标样式
                    g.DrawRectangle(System.Drawing.Pens.Black, 10, 10, THUMBNAIL_WIDTH - 20, THUMBNAIL_HEIGHT - 20);
                    g.DrawLine(System.Drawing.Pens.Black, 10, THUMBNAIL_HEIGHT / 2, THUMBNAIL_WIDTH - 10, THUMBNAIL_HEIGHT / 2);
                    g.DrawLine(System.Drawing.Pens.Black, THUMBNAIL_WIDTH / 2, 10, THUMBNAIL_WIDTH / 2, THUMBNAIL_HEIGHT - 10);
                    
                    // 绘制DWG字样
                    System.Drawing.Font font = new System.Drawing.Font("Arial", 12, System.Drawing.FontStyle.Bold);
                    System.Drawing.Brush brush = System.Drawing.Brushes.Black;
                    g.DrawString("DWG", font, brush, (THUMBNAIL_WIDTH - 30) / 2, (THUMBNAIL_HEIGHT - 15) / 2);
                }
                
                // 转换为BitmapImage
                BitmapImage bitmapImage = new BitmapImage();
                using (MemoryStream stream = new MemoryStream())
                {
                    bmp.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                    stream.Position = 0;
                    bitmapImage.BeginInit();
                    bitmapImage.StreamSource = stream;
                    bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                    bitmapImage.EndInit();
                    bitmapImage.Freeze();
                }
                
                bmp.Dispose();
                
                // 验证默认缩略图是否有效
                if (bitmapImage == null || bitmapImage.Width == 0 || bitmapImage.Height == 0)
                {
                    System.Diagnostics.Debug.WriteLine("生成的默认缩略图无效");
                    // 创建一个最简单的默认缩略图
                    return CreateFallbackThumbnail();
                }
                
                return bitmapImage;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"创建默认缩略图错误: {ex.Message}");
                // 创建一个最简单的默认缩略图
                return CreateFallbackThumbnail();
            }
        }
        
        /// <summary>
        /// 创建一个最简单的默认缩略图，作为最后的 fallback
        /// </summary>
        /// <returns>默认缩略图</returns>
        private static BitmapImage CreateFallbackThumbnail()
        {
            try
            {
                // 创建一个红色的纯色图像作为最后的 fallback
                BitmapImage fallbackImage = new BitmapImage();
                byte[] redPixelData = new byte[4]; // RGBA格式，红色
                redPixelData[0] = 255; // R
                redPixelData[1] = 0;   // G
                redPixelData[2] = 0;   // B
                redPixelData[3] = 255; // A
                
                // 创建一个1x1的红色图像
                Bitmap bmp = new Bitmap(1, 1);
                bmp.SetPixel(0, 0, System.Drawing.Color.FromArgb(255, 255, 0, 0));
                
                using (MemoryStream stream = new MemoryStream())
                {
                    bmp.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                    stream.Position = 0;
                    fallbackImage.BeginInit();
                    fallbackImage.StreamSource = stream;
                    fallbackImage.CacheOption = BitmapCacheOption.OnLoad;
                    fallbackImage.EndInit();
                    fallbackImage.Freeze();
                }
                
                bmp.Dispose();
                return fallbackImage;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"创建 fallback 缩略图错误: {ex.Message}");
                // 作为最后的最后的 fallback，返回一个空的 BitmapImage
                return new BitmapImage();
            }
        }
        
        /// <summary>
        /// 清理过期的缩略图，删除不再有对应DWG文件的缩略图
        /// </summary>
        public static void CleanupExpiredThumbnails()
        {
            try
            {
                string thumbnailFolder = GetThumbnailFolderPath();
                string libraryPath = LibraryManagerService.GetLibraryPath();
                
                if (!Directory.Exists(thumbnailFolder))
                {
                    return;
                }
                
                // 获取所有缩略图文件
                string[] thumbnailFiles = Directory.GetFiles(thumbnailFolder, "*.png");
                
                // 获取所有DWG文件的哈希值，用于检查缩略图是否还有对应的DWG文件
                HashSet<string> existingFileHashes = new HashSet<string>();
                string[] dwgFiles = Directory.GetFiles(libraryPath, "*.dwg", SearchOption.AllDirectories);
                foreach (string dwgFile in dwgFiles)
                {
                    try
                    {
                        string fileHash = LibraryManagerService.CalculateFileHash(dwgFile);
                        existingFileHashes.Add(fileHash);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"计算文件哈希值错误: {ex.Message}");
                    }
                }
                
                // 遍历所有缩略图，删除没有对应DWG文件的缩略图
                foreach (string thumbnailFile in thumbnailFiles)
                {
                    string thumbnailHash = Path.GetFileNameWithoutExtension(thumbnailFile);
                    if (!existingFileHashes.Contains(thumbnailHash))
                    {
                        try
                        {
                            File.Delete(thumbnailFile);
                            System.Diagnostics.Debug.WriteLine($"删除过期缩略图: {thumbnailFile}");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"删除缩略图错误: {ex.Message}");
                        }
                    }
                }
                
                // 清理缓存
                ClearCache();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"清理过期缩略图错误: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 清除缩略图缓存
        /// </summary>
        public static void ClearCache()
        {
            lock (cacheLock)
            {
                thumbnailCache.Clear();
            }
        }
        
        /// <summary>
        /// 预加载缩略图到缓存中
        /// </summary>
        /// <param name="filePaths">文件路径列表</param>
        public static async Task PreloadThumbnailsAsync(List<string> filePaths)
        {
            var tasks = new List<Task>();
            
            foreach (string filePath in filePaths)
            {
                tasks.Add(Task.Run(() => GenerateThumbnail(filePath)));
            }
            
            await Task.WhenAll(tasks);
        }
        
        /// <summary>
        /// 尝试从DWG文件中提取缩略图
        /// </summary>
        private static BitmapImage TryExtractDwgThumbnail(string dwgFilePath)
        {
            try
            {
                // 优先读取DWG内置预览（最可能获得真实缩略图）
                System.Diagnostics.Debug.WriteLine($"[缩略图] 尝试读取DWG内置预览: {Path.GetFileName(dwgFilePath)}");
                BitmapImage dwgPreview = ReadDwgPreviewImage(dwgFilePath);
                if (dwgPreview != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[缩略图] 成功获取DWG内置预览");
                    return dwgPreview;
                }
                
                // 降级到Windows Shell（获取文件图标）
                System.Diagnostics.Debug.WriteLine($"[缩略图] 尝试从Shell获取图标");
                BitmapImage shellThumbnail = GetThumbnailFromShell(dwgFilePath, THUMBNAIL_WIDTH, THUMBNAIL_HEIGHT);
                if (shellThumbnail != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[缩略图] 成功获取Shell图标");
                    return shellThumbnail;
                }
                
                System.Diagnostics.Debug.WriteLine($"[缩略图] 无法获取真实缩略图，将使用动态生成的缩略图");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"提取DWG缩略图失败: {ex.Message}");
            }
            
            return null;
        }
        
        /// <summary>
        /// 从Windows Shell获取文件缩略图（使用简单安全的方法）
        /// </summary>
        private static BitmapImage GetThumbnailFromShell(string filePath, int width, int height)
        {
            try
            {
                // 使用最简单安全的方法：Icon.ExtractAssociatedIcon
                System.Drawing.Icon icon = System.Drawing.Icon.ExtractAssociatedIcon(filePath);
                if (icon != null)
                {
                    using (System.Drawing.Bitmap iconBitmap = icon.ToBitmap())
                    {
                        // 缩放图像
                        using (System.Drawing.Bitmap scaledBitmap = new System.Drawing.Bitmap(iconBitmap, width, height))
                        {
                            BitmapImage bitmapImage = new BitmapImage();
                            using (MemoryStream memoryStream = new MemoryStream())
                            {
                                scaledBitmap.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Png);
                                memoryStream.Position = 0;
                                bitmapImage.BeginInit();
                                bitmapImage.StreamSource = memoryStream;
                                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                                bitmapImage.EndInit();
                                bitmapImage.Freeze();
                            }
                            return bitmapImage;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Shell缩略图获取异常: {ex.Message}");
            }
            
            return null;
        }
        
        /// <summary>
        /// 读取DWG文件的预览图像
        /// 经过验证的可靠方法
        /// </summary>
        private static BitmapImage ReadDwgPreviewImage(string dwgFilePath)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[DWG预览] 开始读取DWG预览: {Path.GetFileName(dwgFilePath)}");
                
                using (var fs = new FileStream(dwgFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new BinaryReader(fs))
                {
                    long fileSize = fs.Length;
                    System.Diagnostics.Debug.WriteLine($"[DWG预览] 文件大小: {fileSize} 字节");
                    
                    if (fileSize < 1000) 
                    {
                        System.Diagnostics.Debug.WriteLine($"[DWG预览] 文件太小，跳过");
                        return null;
                    }
                    
                    // 方法1: 查找文件末尾的DWG预览标记 (R14及以上)
                    long searchStart = Math.Max(0, fileSize - 256 * 1024); // 搜索最后256KB
                    fs.Position = searchStart;
                    byte[] buffer = new byte[fileSize - searchStart];
                    fs.Read(buffer, 0, buffer.Length);
                    
                    System.Diagnostics.Debug.WriteLine($"[DWG预览] 搜索范围: 从偏移 {searchStart} 开始，长度 {buffer.Length}");
                    
                    // 方法1: 查找BMP格式预览 (BM header)
                    int bmpIndex = FindPattern(buffer, new byte[] { 0x42, 0x4D });
                    if (bmpIndex >= 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[DWG预览] 找到BMP预览，偏移: {bmpIndex}");
                        BitmapImage result = TryParseBmpPreview(buffer, bmpIndex);
                        if (result != null) return result;
                    }
                    
                    // 方法2: 查找WMF格式预览 (R14+格式 - D7 CD C6 9A)
                    int wmfIndex = FindPattern(buffer, new byte[] { 0xD7, 0xCD, 0xC6, 0x9A });
                    if (wmfIndex >= 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[DWG预览] 找到WMF预览 (R14格式)，偏移: {wmfIndex}");
                        BitmapImage result = TryParseWmfPreview(buffer, wmfIndex);
                        if (result != null) return result;
                    }
                    
                    // 方法3: 查找WMF格式预览 (旧格式 - 01 00 09 00)
                    wmfIndex = FindPattern(buffer, new byte[] { 0x01, 0x00, 0x09, 0x00 });
                    if (wmfIndex >= 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[DWG预览] 找到WMF预览 (旧格式)，偏移: {wmfIndex}");
                        BitmapImage result = TryParseWmfPreview(buffer, wmfIndex);
                        if (result != null) return result;
                    }
                    
                    // 方法4: 查找ACDSee缩略图格式 (常见于某些DWG)
                    int acdseeIndex = FindPattern(buffer, new byte[] { 0x41, 0x43, 0x44, 0x53 });
                    if (acdseeIndex >= 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[DWG预览] 找到ACDSee预览，偏移: {acdseeIndex}");
                        // 跳过ACDSee头，查找后面的BMP
                        int bmpAfterAcdsee = FindPattern(buffer, new byte[] { 0x42, 0x4D }, acdseeIndex + 100);
                        if (bmpAfterAcdsee >= 0)
                        {
                            BitmapImage result = TryParseBmpPreview(buffer, bmpAfterAcdsee);
                            if (result != null) return result;
                        }
                    }
                    
                    // 方法5: 查找PNG预览格式
                    int pngIndex = FindPattern(buffer, new byte[] { 0x89, 0x50, 0x4E, 0x47 });
                    if (pngIndex >= 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[DWG预览] 找到PNG预览，偏移: {pngIndex}");
                        BitmapImage result = TryParsePngPreview(buffer, pngIndex);
                        if (result != null) return result;
                    }
                    
                    // 方法6: 尝试搜索整个文件（不只最后256KB）
                    System.Diagnostics.Debug.WriteLine($"[DWG预览] 尝试在整个文件中搜索预览...");
                    fs.Position = 0;
                    byte[] fullBuffer = new byte[Math.Min(fileSize, 512 * 1024)]; // 最多读取512KB
                    fs.Read(fullBuffer, 0, fullBuffer.Length);
                    
                    // 在整个缓冲区中搜索
                    bmpIndex = FindPattern(fullBuffer, new byte[] { 0x42, 0x4D });
                    if (bmpIndex >= 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[DWG预览] 在整个文件中找到BMP预览，偏移: {bmpIndex}");
                        BitmapImage result = TryParseBmpPreview(fullBuffer, bmpIndex);
                        if (result != null) return result;
                    }
                    
                    wmfIndex = FindPattern(fullBuffer, new byte[] { 0xD7, 0xCD, 0xC6, 0x9A });
                    if (wmfIndex >= 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[DWG预览] 在整个文件中找到WMF预览，偏移: {wmfIndex}");
                        BitmapImage result = TryParseWmfPreview(fullBuffer, wmfIndex);
                        if (result != null) return result;
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"[DWG预览] 未找到任何预览数据");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"读取DWG预览失败: {ex.Message}");
            }
            
            return null;
        }
        
        private static BitmapImage TryParseBmpPreview(byte[] buffer, int index)
        {
            try
            {
                int bmpDataSize = BitConverter.ToInt32(buffer, index + 2);
                System.Diagnostics.Debug.WriteLine($"[DWG预览] BMP大小: {bmpDataSize} 字节");
                
                if (bmpDataSize > 0 && bmpDataSize < 10 * 1024 * 1024)
                {
                    byte[] bmpData = new byte[bmpDataSize];
                    Array.Copy(buffer, index, bmpData, 0, Math.Min(bmpDataSize, buffer.Length - index));
                    
                    using (var ms = new MemoryStream(bmpData))
                    using (var bmp = new System.Drawing.Bitmap(ms))
                    using (var scaledBmp = new System.Drawing.Bitmap(bmp, THUMBNAIL_WIDTH, THUMBNAIL_HEIGHT))
                    {
                        System.Diagnostics.Debug.WriteLine($"[DWG预览] BMP读取成功，尺寸: {bmp.Width}x{bmp.Height}");
                        return ConvertBitmapToBitmapImage(scaledBmp);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DWG预览] BMP解析失败: {ex.Message}");
            }
            return null;
        }
        
        private static BitmapImage TryParseWmfPreview(byte[] buffer, int index)
        {
            try
            {
                byte[] wmfData = new byte[buffer.Length - index];
                Array.Copy(buffer, index, wmfData, 0, wmfData.Length);
                
                using (var ms = new MemoryStream(wmfData))
                using (var metafile = new System.Drawing.Imaging.Metafile(ms))
                using (var bitmap = new System.Drawing.Bitmap(THUMBNAIL_WIDTH, THUMBNAIL_HEIGHT))
                {
                    using (var g = System.Drawing.Graphics.FromImage(bitmap))
                    {
                        g.Clear(System.Drawing.Color.White);
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                        g.DrawImage(metafile, 0, 0, THUMBNAIL_WIDTH, THUMBNAIL_HEIGHT);
                    }
                    System.Diagnostics.Debug.WriteLine($"[DWG预览] WMF读取成功");
                    return ConvertBitmapToBitmapImage(bitmap);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DWG预览] WMF解析失败: {ex.Message}");
            }
            return null;
        }
        
        private static BitmapImage TryParsePngPreview(byte[] buffer, int index)
        {
            try
            {
                // 查找PNG结束标记
                int pngEnd = FindPattern(buffer, new byte[] { 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82 }, index);
                if (pngEnd >= 0)
                {
                    int pngSize = pngEnd - index + 8;
                    byte[] pngData = new byte[pngSize];
                    Array.Copy(buffer, index, pngData, 0, pngSize);
                    
                    using (var ms = new MemoryStream(pngData))
                    using (var bmp = new System.Drawing.Bitmap(ms))
                    using (var scaledBmp = new System.Drawing.Bitmap(bmp, THUMBNAIL_WIDTH, THUMBNAIL_HEIGHT))
                    {
                        System.Diagnostics.Debug.WriteLine($"[DWG预览] PNG读取成功");
                        return ConvertBitmapToBitmapImage(scaledBmp);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DWG预览] PNG解析失败: {ex.Message}");
            }
            return null;
        }
        
        /// <summary>
        /// 在字节数组中查找模式
        /// </summary>
        private static int FindPattern(byte[] source, byte[] pattern, int startIndex = 0)
        {
            if (pattern.Length == 0 || source.Length < pattern.Length)
                return -1;
            
            for (int i = startIndex; i <= source.Length - pattern.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (source[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                    return i;
            }
            return -1;
        }
        
        private static BitmapImage ConvertBitmapToBitmapImage(System.Drawing.Bitmap bitmap)
        {
            BitmapImage bitmapImage = new BitmapImage();
            using (MemoryStream memoryStream = new MemoryStream())
            {
                bitmap.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Png);
                memoryStream.Position = 0;
                bitmapImage.BeginInit();
                bitmapImage.StreamSource = memoryStream;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();
                bitmapImage.Freeze();
            }
            return bitmapImage;
        }
        
        /// <summary>
        /// 在字节数组中查找模式
        /// </summary>
        private static int FindPattern(byte[] data, byte[] pattern)
        {
            for (int i = 0; i <= data.Length - pattern.Length; i++)
            {
                bool found = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (data[i + j] != pattern[j])
                    {
                        found = false;
                        break;
                    }
                }
                if (found) return i;
            }
            return -1;
        }
        
        /// <summary>
        /// 将BitmapImage保存为PNG文件
        /// </summary>
        private static void SaveBitmapToPngFile(BitmapImage bitmapImage, string filePath)
        {
            BitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmapImage));
            using (FileStream fileStream = new FileStream(filePath, FileMode.Create))
            {
                encoder.Save(fileStream);
            }
        }
    }
}