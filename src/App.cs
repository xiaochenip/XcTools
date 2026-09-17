using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using XcTools.Services;

namespace XcTools
{
    public class App : IExtensionApplication
    {
        public static string LibraryPath { get; private set; }
        
        public static string CurrentSelectedFilePath { get; set; }
        
        public static string CurrentSelectedCategoryPath { get; set; }
        
        public static bool SkipScalePrompt { get; set; }
        
        public static double InsertScale { get; set; } = 1.0;
        
        public static double InsertRotation { get; set; } = 0.0;
        
        public static string AddToLibraryCategoryPath { get; set; }

        private static string _logPath;
        
        public static void Log(string message)
        {
            try
            {
                if (_logPath == null)
                {
                    string assemblyPath = Assembly.GetExecutingAssembly().Location;
                    string dir = Path.GetDirectoryName(assemblyPath);
                    _logPath = Path.Combine(dir, "XcTools.log");
                }
                string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";
                File.AppendAllText(_logPath, logEntry);
            }
            catch { }
        }

        public static void SetLibraryPath(string path)
        {
            LibraryPath = path;
        }

        /// <summary>
        /// 插件根目录（XcTools.lsp / library / .cuix 所在层）。
        /// 多框架发布时 DLL 位于 net8/net48 子目录，此时取其父目录。
        /// </summary>
        public static string PluginDir
        {
            get
            {
                string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string name = Path.GetFileName(dir);
                if (name == "net8" || name == "net48")
                {
                    string parent = Path.GetDirectoryName(dir);
                    if (!string.IsNullOrEmpty(parent)) return parent;
                }
                return dir;
            }
        }

        /// <summary>
        /// 从插件包根目录加载窗口图标 favicon.ico。
        /// 图标随包分发（与 XcTools.lsp 同层），运行时按文件路径读取，
        /// 避免 pack URI 在 AutoCAD 宿主进程中指向 acad.exe 而失效。
        /// </summary>
        public static System.Windows.Media.ImageSource LoadWindowIcon()
        {
            try
            {
                string iconPath = Path.Combine(PluginDir, "favicon.ico");
                if (!File.Exists(iconPath))
                {
                    Log($"LoadWindowIcon: 未找到图标文件 {iconPath}");
                    return null;
                }
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(iconPath, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                Log($"LoadWindowIcon OK: {iconPath}");
                return bmp;
            }
            catch (System.Exception ex)
            {
                Log($"LoadWindowIcon ERROR: {ex.Message}");
                return null;
            }
        }

        public void Initialize()
        {
            Log("=== Initialize() START ===");
            Log($"Assembly location: {Assembly.GetExecutingAssembly().Location}");
            
            try
            {
                Log("Step 1: Initializing library...");
                InitializeLibrary();
                Log($"Step 1: Library initialized. Path: {LibraryPath}");

                Log("Step 1.5: Registering auto-loader (acad.lsp)...");
                RegisterAutoLoader();
                
                Log("Step 2: Getting document...");
                Document doc = AcApp.DocumentManager.MdiActiveDocument;
                if (doc != null)
                {
                    ed = doc.Editor;
                    Log("Step 2: Document found, editor assigned.");
                    
                    ed.WriteMessage("\n========================================");
                    ed.WriteMessage("\n小辰CAD工具箱 v1.0.1 已加载！");
                    ed.WriteMessage("\n========================================");
                    ed.WriteMessage("\n使用命令:");
                    ed.WriteMessage("\n  XCH         - 命令列表");
                    ed.WriteMessage("\n  XTK         - 图库管理");
                    ed.WriteMessage("\n  XCI         - 插入图块");
                    ed.WriteMessage("\n  XCM         - 连续插入");
                    ed.WriteMessage("\n  XAD         - 选图入库");
                    ed.WriteMessage("\n  XSETLIBPATH - 设置图库路径");
                    ed.WriteMessage("\n  XLIBPATH    - 查看当前图库路径");
                    ed.WriteMessage("\n  XLOADUI     - 加载菜单栏界面");
                    ed.WriteMessage("\n  XCMENU      - 加载菜单栏");
                    ed.WriteMessage("\n========================================");
                }
                else
                {
                    Log("Step 2: No MDI document available (CAD may not have a document open yet)");
                }
                
                Log("Step 3: Registering Idle event...");
                AcApp.Idle += OnIdle;
                Log("Step 3: Idle event registered.");
                Log("=== Initialize() COMPLETE ===");
            }
            catch (System.Exception ex)
            {
                Log($"Initialize ERROR: {ex.Message}");
                Log($"Stack trace: {ex.StackTrace}");
                if (AcApp.DocumentManager.MdiActiveDocument != null)
                {
                    try
                    {
                        AcApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\n小辰CAD工具箱加载错误: {ex.Message}");
                    }
                    catch { }
                }
            }
        }

        private Editor ed;
        private bool _interfaceLoaded;
        private bool _documentActivatedHooked;
        private readonly DocumentCollectionEventHandler _documentActivatedHandler;

        public App()
        {
            _documentActivatedHandler =
                new DocumentCollectionEventHandler(OnDocumentActivatedForInterface);
        }

        /// <summary>当编辑器就绪（OnIdle 时无文档）时 DocumentActivated 触发→重试加载菜单</summary>
        private void OnDocumentActivatedForInterface(object sender, DocumentCollectionEventArgs e)
        {
            try
            {
                if (_interfaceLoaded) return;
                Document doc = AcApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                ed = doc.Editor;
                Log("OnDocumentActivatedForInterface: Retrying default interface load...");
                LoadDefaultInterface();
                _interfaceLoaded = true;
            }
            catch (System.Exception ex)
            {
                Log($"OnDocumentActivatedForInterface ERROR: {ex.Message}");
            }
            finally
            {
                if (_interfaceLoaded && _documentActivatedHooked)
                {
                    AcApp.DocumentManager.DocumentActivated -= _documentActivatedHandler;
                    _documentActivatedHooked = false;
                }
            }
        }

        /// <summary>
        /// 把 load 语句写入 acad.lsp —— AutoCAD 启动必执行，本插件唯一的
        /// 自动加载机制（实测最可靠；Applications 需求加载注册表会被
        /// AutoCAD 维护时清除，APPLOAD 启动组注册表写入会被退出时覆盖，
        /// 两者均已废弃不用，避免产生多余加载弹窗）。
        ///
        /// 注意：多框架发布时 DLL 位于 net8/ 或 net48/ 子目录，
        /// XcTools.lsp 在其父目录，需向上查找。
        /// </summary>
        private static void RegisterAutoLoader()
        {
            try
            {
                string dllPath = Assembly.GetExecutingAssembly().Location;
                string dir = Path.GetDirectoryName(dllPath);
                string lspPath = FindLspFile(dir);

                if (lspPath == null)
                {
                    Log($"RegisterAutoLoader: XcTools.lsp not found near {dllPath}, skip.");
                    return;
                }

                RegisterAcadLspLoader(lspPath);
            }
            catch (System.Exception ex)
            {
                Log($"RegisterAutoLoader ERROR: {ex.Message}");
            }
        }

        /// <summary>在目录本身、父目录中查找 XcTools.lsp（多框架布局：net8/net48 子目录）。</summary>
        private static string FindLspFile(string dir)
        {
            for (int i = 0; i < 2 && dir != null; i++)
            {
                string candidate = Path.Combine(dir, "XcTools.lsp");
                if (File.Exists(candidate)) return candidate;
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }

        /// <summary>将一行 (load) 语句写入各版本的 acad.lsp（AutoCAD 启动必执行）</summary>
        private static void RegisterAcadLspLoader(string lspPath)
        {
            try
            {
                string loadLine = "(vl-catch-all-apply (function load) (list \""
                    + lspPath.Replace("\\", "/") + "\"))";

                string[] roots = new[] { Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                         Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) };
                foreach (string root in roots)
                {
                    if (string.IsNullOrEmpty(root)) continue;
                    string ad = Path.Combine(root, "Autodesk");
                    if (!Directory.Exists(ad)) continue;
                    foreach (string supportDir in Directory.EnumerateDirectories(ad, "Support", SearchOption.AllDirectories))
                    {
                        // 不按版本过滤：写入本机全部 AutoCAD（2021-2026）的 Support 目录，
                        // 每个版本的 AutoCAD 启动时各自加载 acad.lsp → 由 LSP 按版本选 net8/net48 DLL
                        string acadLsp = Path.Combine(supportDir, "acad.lsp");
                        try
                        {
                            string content = File.Exists(acadLsp) ? File.ReadAllText(acadLsp) : "";
                            if (content.Contains(lspPath.Replace("\\", "/")) || content.Contains(lspPath))
                            {
                                Log($"RegisterAcadLspLoader: Already in place: {acadLsp}");
                                continue;
                            }
                            using (var sw = File.AppendText(acadLsp))
                            {
                                sw.WriteLine();
                                sw.WriteLine(";; XcTools autoloader (added by XcTools.dll)");
                                sw.WriteLine(loadLine);
                            }
                            Log($"RegisterAcadLspLoader: Patched: {acadLsp}");
                        }
                        catch (System.Exception ex)
                        {
                            Log($"RegisterAcadLspLoader: Skip {acadLsp}: {ex.Message}");
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log($"RegisterAcadLspLoader ERROR: {ex.Message}");
            }
        }

        /// <summary>
        /// 从各版本 acad.lsp 中移除本插件写入的自动加载登记。
        /// 与 RegisterAcadLspLoader 配对：扫描同样的 Support 目录，按行剔除
        /// 「XcTools autoloader」注释行和包含 XcTools.lsp 的 (load) 行。
        /// </summary>
        public static int UnregisterAcadLspLoader()
        {
            int removedFiles = 0;
            try
            {
                string[] roots = new[] { Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                         Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) };
                foreach (string root in roots)
                {
                    if (string.IsNullOrEmpty(root)) continue;
                    string ad = Path.Combine(root, "Autodesk");
                    if (!Directory.Exists(ad)) continue;
                    foreach (string supportDir in Directory.EnumerateDirectories(ad, "Support", SearchOption.AllDirectories))
                    {
                        string acadLsp = Path.Combine(supportDir, "acad.lsp");
                        if (!File.Exists(acadLsp)) continue;
                        try
                        {
                            string[] lines = File.ReadAllLines(acadLsp);
                            var keep = new List<string>(lines.Length);
                            int removed = 0;
                            for (int i = 0; i < lines.Length; i++)
                            {
                                string line = lines[i];
                                bool isComment = line.Contains("XcTools autoloader");
                                bool isLoad = line.Contains("XcTools.lsp") && line.Contains("load");
                                if (isComment || isLoad)
                                {
                                    removed++;
                                    // 注释行 + load 行一起删，且删掉紧跟其后的一个空行
                                    if (isComment && i + 1 < lines.Length && lines[i + 1].Contains("XcTools.lsp"))
                                        continue; // 下一轮处理 load 行
                                    if (i + 1 < lines.Length && string.IsNullOrWhiteSpace(lines[i + 1]))
                                        i++; // 跳过紧随的空行
                                    continue;
                                }
                                keep.Add(line);
                            }
                            if (removed > 0)
                            {
                                File.WriteAllLines(acadLsp, keep);
                                Log($"UnregisterAcadLspLoader: cleaned {acadLsp} ({removed} lines removed)");
                                removedFiles++;
                            }
                        }
                        catch (System.Exception ex)
                        {
                            Log($"UnregisterAcadLspLoader: Skip {acadLsp}: {ex.Message}");
                        }
                    }
                }
                Log($"UnregisterAcadLspLoader COMPLETE: {removedFiles} file(s) updated.");
            }
            catch (System.Exception ex)
            {
                Log($"UnregisterAcadLspLoader ERROR: {ex.Message}");
            }
            return removedFiles;
        }
        
        private void OnIdle(object sender, EventArgs e)
        {
            Log("=== OnIdle() triggered ===");
            
            if (_interfaceLoaded)
            {
                Log("OnIdle: Already loaded, removing handler.");
                AcApp.Idle -= OnIdle;
                return;
            }
            
            try
            {
                Log("OnIdle: Loading default interface...");
                bool hadDocBefore = (AcApp.DocumentManager.MdiActiveDocument != null);
                LoadDefaultInterface();
                _interfaceLoaded = hadDocBefore && CuixManager.IsMenuLoaded;
                if (!_interfaceLoaded && !_documentActivatedHooked)
                {
                    // 源泉设计等启动脚本会切文档：活动文档临时变 null，等 DocumentActivated 再重试
                    AcApp.DocumentManager.DocumentActivated += _documentActivatedHandler;
                    _documentActivatedHooked = true;
                    Log("OnIdle: No usable document now, DocumentActivated hook installed.");
                }
                AcApp.Idle -= OnIdle;
                Log($"OnIdle: Interface loaded={_interfaceLoaded}, docActivatedHooked={_documentActivatedHooked}");
            }
            catch (System.Exception ex)
            {
                Log($"OnIdle ERROR: {ex.Message}");
                Log($"Stack trace: {ex.StackTrace}");
                if (ed != null)
                {
                    try
                    {
                        ed.WriteMessage($"\n加载界面错误: {ex.Message}");
                    }
                    catch { }
                }
            }
        }

        private void InitializeLibrary()
        {
            try
            {
                LibraryPath = LibraryManagerService.GetLibraryPath();
                Log($"Library path resolved: {LibraryPath}");
                
                LibraryManagerService.EnsureLibraryFolderExists();
                Log("Library folder ensured.");
                
                string defaultPath = LibraryManagerService.GetDefaultLibraryPath();
                Log($"Default library path: {defaultPath}");
                
                if (LibraryPath.Equals(defaultPath, StringComparison.OrdinalIgnoreCase))
                {
                    Log("Using default path, ensuring categories exist...");
                    LibraryManagerService.EnsureDefaultCategoriesExists();
                    Log("Default categories ensured.");
                }
            }
            catch (System.Exception ex)
            {
                Log($"InitializeLibrary ERROR: {ex.Message}");
                Log($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        private void LoadDefaultInterface()
        {
            try
            {
                Log("LoadDefaultInterface: Loading menu bar via CUI...");
                CuixManager.LoadDefaultInterface();
                Log($"LoadDefaultInterface: Menu loaded={CuixManager.IsMenuLoaded}");
                // 菜单栏由 AutoCAD 启动时的 Partial CUIX 自动加载，
                // 这里不再向命令行打印提示，保持启动输出干净。
            }
            catch (System.Exception ex)
            {
                Log($"LoadDefaultInterface ERROR: {ex.Message}");
                Log($"Stack trace: {ex.StackTrace}");
                if (ed != null)
                {
                    try
                    {
                        ed.WriteMessage($"\n加载界面失败: {ex.Message}");
                        ed.WriteMessage("\n可手动使用 XLOADUI 命令加载界面");
                    }
                    catch { }
                }
            }
        }

        public void Terminate()
        {
            Log("=== Terminate() ===");
            try
            {
                // 注意：不在退出时 CUIUNLOAD 菜单组！
                // 部分菜单随宿主持久化是 AutoCAD 标准行为；退出时卸载会在工作区
                // 留下"悬挂"菜单项（所属组已卸载但菜单栏仍引用），下次点击该残留项
                // 会把原始宏文本直接送到命令行，报 未知命令"^C^C_XXX"。

                if (AcApp.DocumentManager.MdiActiveDocument != null)
                {
                    Editor ed = AcApp.DocumentManager.MdiActiveDocument.Editor;
                    ed.WriteMessage("\n小辰CAD工具箱已卸载！");
                }
            }
            catch (System.Exception ex)
            {
                Log($"Terminate ERROR: {ex.Message}");
                if (AcApp.DocumentManager.MdiActiveDocument != null)
                {
                    AcApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\n卸载错误: {ex.Message}");
                }
            }
        }
    }
}
