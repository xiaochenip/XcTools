using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using XcTools.Services;
using XcTools.Views;

namespace XcTools.Commands
{
    public class Commands
    {
        [CommandMethod("XCCAD_LIBRARY", CommandFlags.Session)]
        public void OpenLibraryManager()
        {
            try
            {
                // 打开图库管理器窗口
                Views.LibraryManagerView view = new Views.LibraryManagerView();
                // 在AutoCAD环境中，WPF Application.Current.MainWindow可能为null，因此不设置Owner
                view.ShowDialog();
            }
            catch (System.Exception ex)
            {
                // Session 模式下可能没有打开的图纸
                AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage($"\n打开图库管理器错误: {ex.Message}");
            }
        }

        [CommandMethod("XCCAD_QUICKACTIONS", CommandFlags.Session)]
        public void OpenQuickActions()
        {
            try
            {
                // 打开快捷操作窗口
                Views.QuickActionsView view = new Views.QuickActionsView();
                // 在AutoCAD环境中，WPF Application.Current.MainWindow可能为null，因此不设置Owner
                view.ShowDialog();
            }
            catch (System.Exception ex)
            {
                // Session 模式下可能没有打开的图纸
                AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage($"\n打开快捷操作错误: {ex.Message}");
            }
        }

        [CommandMethod("XCCAD_INSERTDWG", CommandFlags.Modal)]
        public void InsertDWGFromLibrary()
        {
            try
            {
                string selectedFile = App.CurrentSelectedFilePath;
                bool isAutoSelected = !string.IsNullOrEmpty(selectedFile);

                // 如果没有自动选择的文件，则让用户选择
                if (!isAutoSelected)
                {
                    // 选择要插入的DWG文件，搜索所有子目录
                    string[] dwgFiles = Directory.GetFiles(App.LibraryPath, "*.dwg", SearchOption.AllDirectories);
                    if (dwgFiles.Length == 0)
                    {
                        AcApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\n图库中没有DWG文件！");
                        return;
                    }

                    // 创建文件选择器，使用字典存储文件名到完整路径的映射
                    Dictionary<string, string> fileNameToPathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string filePath in dwgFiles)
                    {
                        string fileName = Path.GetFileName(filePath);
                        // 如果文件名重复，添加序号以区分
                        int counter = 1;
                        string uniqueFileName = fileName;
                        while (fileNameToPathMap.ContainsKey(uniqueFileName))
                        {
                            uniqueFileName = $"{Path.GetFileNameWithoutExtension(fileName)}({counter}){Path.GetExtension(fileName)}";
                            counter++;
                        }
                        fileNameToPathMap[uniqueFileName] = filePath;
                    }

                    PromptKeywordOptions pko = new PromptKeywordOptions("\n选择要插入的图形:");
                    // 使用逐个添加代替AddRange
                    foreach (string uniqueFileName in fileNameToPathMap.Keys)
                    {
                        pko.Keywords.Add(uniqueFileName);
                    }
                    pko.AllowNone = true;

                    PromptResult pr = AcApp.DocumentManager.MdiActiveDocument.Editor.GetKeywords(pko);
                    if (pr.Status != PromptStatus.OK)
                        return;

                    // 从映射中获取完整路径
                    if (fileNameToPathMap.TryGetValue(pr.StringResult, out string fullPath))
                    {
                        selectedFile = fullPath;
                    }
                    else
                    {
                        AcApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\n找不到文件: {pr.StringResult}");
                        return;
                    }
                }
                else
                {
                    // 清空自动选择的文件路径，以便下次使用
                    App.CurrentSelectedFilePath = null;
                }

                // 获取插入比例（默认1:1）
                Editor editor = AcApp.DocumentManager.MdiActiveDocument.Editor;
                double scale = 1.0;
                double rotation = 0.0;
                
                // 询问比例，用户可以直接按空格或回车使用默认值，也可以输入新值后确认
                PromptDoubleOptions pdo = new PromptDoubleOptions("\n输入插入比例");
                pdo.DefaultValue = 1.0;
                pdo.AllowNone = true;
                PromptDoubleResult pdr = editor.GetDouble(pdo);
                
                if (pdr.Status == PromptStatus.OK)
                {
                    scale = pdr.Value;
                    if (scale <= 0)
                    {
                        editor.WriteMessage("\n比例必须大于0，使用默认值1.0");
                        scale = 1.0;
                    }
                }
                
                // 询问旋转角度
                PromptDoubleOptions rdo = new PromptDoubleOptions("\n输入旋转角度");
                rdo.DefaultValue = 0.0;
                rdo.AllowNone = true;
                PromptDoubleResult rdr = editor.GetDouble(rdo);
                
                if (rdr.Status == PromptStatus.OK)
                {
                    rotation = rdr.Value;
                }

                // 获取插入点
                PromptPointOptions ppo = new PromptPointOptions("\n指定插入点:");
                PromptPointResult ppr = editor.GetPoint(ppo);
                if (ppr.Status != PromptStatus.OK)
                    return;

                // 插入DWG文件
                using (Transaction tr = AcApp.DocumentManager.MdiActiveDocument.Database.TransactionManager.StartTransaction())
                {
                    BlockTable bt = tr.GetObject(AcApp.DocumentManager.MdiActiveDocument.Database.BlockTableId, OpenMode.ForRead) as BlockTable;
                    BlockTableRecord btr = tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;

                    // 插入外部DWG作为块
                    using (Database sourceDb = new Database(false, true))
                    {
                        sourceDb.ReadDwgFile(selectedFile, FileOpenMode.OpenForReadAndAllShare, true, "");
                        
                        // 处理块名称，确保它是有效的AutoCAD块名
                        string fileNameWithoutExt = Path.GetFileNameWithoutExtension(selectedFile);
                        // 替换无效字符
                        string validBlockName = System.Text.RegularExpressions.Regex.Replace(fileNameWithoutExt, @"[^a-zA-Z0-9_$#@.-]", "_");
                        
                        // 插入块，使用overwrite=true确保如果块已存在也能正确处理
                        ObjectId blockId = AcApp.DocumentManager.MdiActiveDocument.Database.Insert(validBlockName, sourceDb, true);

                        // 定义块引用
                        BlockReference br = new BlockReference(ppr.Value, blockId);
                        
                        // 设置块引用的缩放（使用用户输入的比例）和旋转
                        br.ScaleFactors = new Scale3d(scale, scale, scale);
                        br.Rotation = rotation * Math.PI / 180.0;
                        
                        // 添加块引用到模型空间
                        btr.AppendEntity(br);
                        tr.AddNewlyCreatedDBObject(br, true);
                        
                        // 处理块属性
                        BlockTableRecord blockDef = tr.GetObject(blockId, OpenMode.ForRead) as BlockTableRecord;
                        foreach (ObjectId id in blockDef)
                        {
                            DBObject obj = tr.GetObject(id, OpenMode.ForRead);
                            AttributeDefinition attDef = obj as AttributeDefinition;
                            if (attDef != null)
                            {
                                // 创建属性引用
                                AttributeReference attRef = new AttributeReference();
                                attRef.SetAttributeFromBlock(attDef, br.BlockTransform);
                                attRef.Position = attDef.Position.TransformBy(br.BlockTransform);
                                
                                // 添加属性引用到块引用
                                br.AttributeCollection.AppendAttribute(attRef);
                                tr.AddNewlyCreatedDBObject(attRef, true);
                            }
                        }
                    }

                    tr.Commit();
                }

                AcApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\n已插入图形: {Path.GetFileName(selectedFile)} (比例: {scale})");
            }
            catch (System.Exception ex)
            {
                AcApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\n插入图形错误: {ex.Message}");
            }
        }

        [CommandMethod("XCCAD_ADDDWG", CommandFlags.Modal)]
        public void AddDWGToLibrary()
        {
            try
            {
                // 打开文件选择对话框
                Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog();
                openFileDialog.Filter = "DWG文件 (*.dwg)|*.dwg|所有文件 (*.*)|*.*";
                openFileDialog.Title = "选择要添加到图库的DWG文件";

                if (openFileDialog.ShowDialog() == true)
                {
                    string sourceFile = openFileDialog.FileName;
                    string destFile = Path.Combine(App.LibraryPath, Path.GetFileName(sourceFile));

                    // 检查文件是否已存在
                    if (File.Exists(destFile))
                    {
                        // 使用MessageBox.Show的替代方法，因为WPF MessageBox在AutoCAD中可能有问题
                        AcApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\n文件 {Path.GetFileName(destFile)} 已存在，是否覆盖？(Y/N): ");
                        string response = Console.ReadLine();
                        if (response?.ToUpper() != "Y")
                            return;
                    }

                    // 复制文件到图库
                    File.Copy(sourceFile, destFile, true);

                    AcApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\n已添加图形到图库: {Path.GetFileName(sourceFile)}");
                }
            }
            catch (System.Exception ex)
            {
                AcApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\n添加图形到图库错误: {ex.Message}");
            }
        }

        // 显示帮助和命令列表（XCH命令）
        [CommandMethod("XCH", CommandFlags.Modal)]
        public void ShowHelpAndCommands()
        {
            try
            {
                ShowHelpDialog();
            }
            catch (System.Exception ex)
            {
                AcApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\n显示命令列表错误: {ex.Message}");
            }
        }

        // 打开图库管理器（XCT命令别名）
        [CommandMethod("XCT", CommandFlags.Session)]
        public void OpenLibraryManagerAlias()
        {
            OpenLibraryManager();
        }

        // 打开快捷操作面板（XCQ命令别名）
        [CommandMethod("XCQ", CommandFlags.Session)]
        public void OpenQuickActionsAlias()
        {
            OpenQuickActions();
        }

        // XTK - 打开/关闭图库主界面
        [CommandMethod("XTK", CommandFlags.Session)]
        public void ToggleLibraryManager()
        {
            try
            {
                OpenLibraryManager();
            }
            catch (System.Exception ex)
            {
                // Session 模式下可能没有打开的图纸
                AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage($"\n打开图库管理器错误: {ex.Message}");
            }
        }

        // XCI - 插入选中图块
        [CommandMethod("XCI", CommandFlags.Modal)]
        public void InsertSelectedBlock()
        {
            InsertDWGFromLibrary();
        }

        // XCM - 连续插入模式
        [CommandMethod("XCM", CommandFlags.Session)]
        public void ContinuousInsertMode()
        {
            // Session 模式下可能没有打开的图纸，插入操作需要图纸
            Editor ed = AcApp.DocumentManager.MdiActiveDocument?.Editor;
            if (ed == null)
            {
                System.Windows.MessageBox.Show("请先打开一个图纸文件，再使用连续插入。",
                    "小辰CAD工具箱", System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
                return;
            }

            try
            {
                ed.WriteMessage("\n连续插入模式已启动！");
                ed.WriteMessage("\n提示：选择一个图块后可以连续插入到多个位置");
                
                string[] dwgFiles = Directory.GetFiles(App.LibraryPath, "*.dwg", SearchOption.AllDirectories);
                if (dwgFiles.Length == 0)
                {
                    ed.WriteMessage("\n图库中没有DWG文件！");
                    return;
                }

                Dictionary<string, string> fileNameToPathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string filePath in dwgFiles)
                {
                    string fileName = Path.GetFileName(filePath);
                    int counter = 1;
                    string uniqueFileName = fileName;
                    while (fileNameToPathMap.ContainsKey(uniqueFileName))
                    {
                        uniqueFileName = $"{Path.GetFileNameWithoutExtension(fileName)}({counter}){Path.GetExtension(fileName)}";
                        counter++;
                    }
                    fileNameToPathMap[uniqueFileName] = filePath;
                }

                PromptKeywordOptions pko = new PromptKeywordOptions("\n选择要连续插入的图形:");
                foreach (string uniqueFileName in fileNameToPathMap.Keys)
                {
                    pko.Keywords.Add(uniqueFileName);
                }
                pko.AllowNone = true;

                PromptResult pr = ed.GetKeywords(pko);
                if (pr.Status != PromptStatus.OK)
                    return;

                string selectedFile = null;
                if (fileNameToPathMap.TryGetValue(pr.StringResult, out string fullPath))
                {
                    selectedFile = fullPath;
                }
                else
                {
                    ed.WriteMessage($"\n找不到文件: {pr.StringResult}");
                    return;
                }

                ed.WriteMessage("\n连续插入模式：点击插入点进行插入，按Esc退出");

                while (true)
                {
                    PromptPointOptions ppo = new PromptPointOptions("\n指定插入点 (按Esc退出):");
                    PromptPointResult ppr = ed.GetPoint(ppo);
                    
                    if (ppr.Status != PromptStatus.OK)
                        break;

                    using (Transaction tr = AcApp.DocumentManager.MdiActiveDocument.Database.TransactionManager.StartTransaction())
                    {
                        BlockTable bt = tr.GetObject(AcApp.DocumentManager.MdiActiveDocument.Database.BlockTableId, OpenMode.ForRead) as BlockTable;
                        BlockTableRecord btr = tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;

                        using (Database sourceDb = new Database(false, true))
                        {
                            sourceDb.ReadDwgFile(selectedFile, FileOpenMode.OpenForReadAndAllShare, true, "");
                            
                            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(selectedFile);
                            string validBlockName = System.Text.RegularExpressions.Regex.Replace(fileNameWithoutExt, @"[^a-zA-Z0-9_$#@.-]", "_");
                            
                            ObjectId blockId = AcApp.DocumentManager.MdiActiveDocument.Database.Insert(validBlockName, sourceDb, true);

                            BlockReference br = new BlockReference(ppr.Value, blockId);
                            br.ScaleFactors = new Scale3d(1.0, 1.0, 1.0);
                            br.Rotation = 0.0;
                            
                            btr.AppendEntity(br);
                            tr.AddNewlyCreatedDBObject(br, true);
                        }

                        tr.Commit();
                    }

                    ed.WriteMessage($"\n已插入图形: {Path.GetFileName(selectedFile)}");
                }

                ed.WriteMessage("\n连续插入模式已退出！");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n连续插入错误: {ex.Message}");
            }
        }

        // XCAD - 将当前图块添加到图库（XAD别名）
        [CommandMethod("XCAD", CommandFlags.Modal)]
        public void AddCurrentBlockToLibraryAlias()
        {
            AddCurrentBlockToLibrary();
        }

        // XCRF - 刷新缩略图
        [CommandMethod("XCRF", CommandFlags.Modal)]
        public void RefreshThumbnails()
        {
            try
            {
                Editor ed = AcApp.DocumentManager.MdiActiveDocument.Editor;
                ed.WriteMessage("\n正在刷新缩略图...");
                
                ThumbnailManagerService.ClearCache();
                ThumbnailManagerService.CleanupExpiredThumbnails();
                
                ed.WriteMessage("\n缩略图刷新完成！");
            }
            catch (System.Exception ex)
            {
                AcApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\n刷新缩略图错误: {ex.Message}");
            }
        }

        // XCFD - 搜索图块
        [CommandMethod("XCFD", CommandFlags.Modal)]
        public void SearchBlocks()
        {
            try
            {
                Editor ed = AcApp.DocumentManager.MdiActiveDocument.Editor;
                
                PromptStringOptions pso = new PromptStringOptions("\n请输入搜索关键词:");
                pso.AllowSpaces = true;
                PromptResult pr = ed.GetString(pso);
                
                if (pr.Status != PromptStatus.OK)
                    return;

                string keyword = pr.StringResult.ToLower();
                
                string[] dwgFiles = Directory.GetFiles(App.LibraryPath, "*.dwg", SearchOption.AllDirectories);
                List<string> matchedFiles = new List<string>();

                foreach (string filePath in dwgFiles)
                {
                    string fileName = Path.GetFileName(filePath).ToLower();
                    if (fileName.Contains(keyword))
                    {
                        matchedFiles.Add(filePath);
                    }
                }

                if (matchedFiles.Count == 0)
                {
                    ed.WriteMessage("\n未找到匹配的图块！");
                    return;
                }

                ed.WriteMessage($"\n找到 {matchedFiles.Count} 个匹配的图块:");
                foreach (string filePath in matchedFiles)
                {
                    ed.WriteMessage($"\n  - {Path.GetFileName(filePath)}");
                }

                ed.WriteMessage("\n提示：使用XTK打开图库管理器查看详细信息");
            }
            catch (System.Exception ex)
            {
                AcApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\n搜索图块错误: {ex.Message}");
            }
        }

        // XCF - 新建分类
        [CommandMethod("XCF", CommandFlags.Modal)]
        public void CreateNewCategory()
        {
            try
            {
                Editor ed = AcApp.DocumentManager.MdiActiveDocument.Editor;
                
                PromptStringOptions pso = new PromptStringOptions("\n请输入新分类名称:");
                pso.AllowSpaces = true;
                PromptResult pr = ed.GetString(pso);
                
                if (pr.Status != PromptStatus.OK)
                    return;

                string categoryName = pr.StringResult;
                
                if (string.IsNullOrWhiteSpace(categoryName))
                {
                    ed.WriteMessage("\n分类名称不能为空！");
                    return;
                }

                if (categoryName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    ed.WriteMessage("\n分类名称包含无效字符！");
                    return;
                }

                string categoryPath = Path.Combine(App.LibraryPath, categoryName);
                
                if (Directory.Exists(categoryPath))
                {
                    ed.WriteMessage("\n分类已存在！");
                    return;
                }

                Directory.CreateDirectory(categoryPath);
                ed.WriteMessage($"\n分类 \"{categoryName}\" 创建成功！");
            }
            catch (System.Exception ex)
            {
                AcApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\n创建分类错误: {ex.Message}");
            }
        }

        // XCREN - 重命名图块
        [CommandMethod("XCREN", CommandFlags.Modal)]
        public void RenameBlock()
        {
            try
            {
                Editor ed = AcApp.DocumentManager.MdiActiveDocument.Editor;
                
                string[] dwgFiles = Directory.GetFiles(App.LibraryPath, "*.dwg", SearchOption.AllDirectories);
                if (dwgFiles.Length == 0)
                {
                    ed.WriteMessage("\n图库中没有DWG文件！");
                    return;
                }

                Dictionary<string, string> fileNameToPathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string filePath in dwgFiles)
                {
                    string fileName = Path.GetFileName(filePath);
                    int counter = 1;
                    string uniqueFileName = fileName;
                    while (fileNameToPathMap.ContainsKey(uniqueFileName))
                    {
                        uniqueFileName = $"{Path.GetFileNameWithoutExtension(fileName)}({counter}){Path.GetExtension(fileName)}";
                        counter++;
                    }
                    fileNameToPathMap[uniqueFileName] = filePath;
                }

                PromptKeywordOptions pko = new PromptKeywordOptions("\n选择要重命名的图块:");
                foreach (string uniqueFileName in fileNameToPathMap.Keys)
                {
                    pko.Keywords.Add(uniqueFileName);
                }
                pko.AllowNone = true;

                PromptResult pr = ed.GetKeywords(pko);
                if (pr.Status != PromptStatus.OK)
                    return;

                string selectedFile = null;
                if (fileNameToPathMap.TryGetValue(pr.StringResult, out string fullPath))
                {
                    selectedFile = fullPath;
                }
                else
                {
                    ed.WriteMessage($"\n找不到文件: {pr.StringResult}");
                    return;
                }

                string fileNameWithoutExt = Path.GetFileNameWithoutExtension(selectedFile);
                
                PromptStringOptions pso = new PromptStringOptions($"\n请输入新名称 (当前: {fileNameWithoutExt}):");
                pso.AllowSpaces = false;
                pr = ed.GetString(pso);
                
                if (pr.Status != PromptStatus.OK)
                    return;

                string newName = pr.StringResult;
                
                if (string.IsNullOrWhiteSpace(newName))
                {
                    ed.WriteMessage("\n文件名不能为空！");
                    return;
                }

                if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    ed.WriteMessage("\n文件名包含无效字符！");
                    return;
                }

                string newFilePath = Path.Combine(Path.GetDirectoryName(selectedFile), newName + ".dwg");
                
                if (File.Exists(newFilePath))
                {
                    ed.WriteMessage("\n文件已存在！");
                    return;
                }

                File.Move(selectedFile, newFilePath);
                ed.WriteMessage($"\n图块已重命名为: {newName}.dwg");
            }
            catch (System.Exception ex)
            {
                AcApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\n重命名图块错误: {ex.Message}");
            }
        }

        // XCDEL - 删除图块
        [CommandMethod("XCDEL", CommandFlags.Modal)]
        public void DeleteBlock()
        {
            try
            {
                Editor ed = AcApp.DocumentManager.MdiActiveDocument.Editor;
                
                string[] dwgFiles = Directory.GetFiles(App.LibraryPath, "*.dwg", SearchOption.AllDirectories);
                if (dwgFiles.Length == 0)
                {
                    ed.WriteMessage("\n图库中没有DWG文件！");
                    return;
                }

                Dictionary<string, string> fileNameToPathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string filePath in dwgFiles)
                {
                    string fileName = Path.GetFileName(filePath);
                    int counter = 1;
                    string uniqueFileName = fileName;
                    while (fileNameToPathMap.ContainsKey(uniqueFileName))
                    {
                        uniqueFileName = $"{Path.GetFileNameWithoutExtension(fileName)}({counter}){Path.GetExtension(fileName)}";
                        counter++;
                    }
                    fileNameToPathMap[uniqueFileName] = filePath;
                }

                PromptKeywordOptions pko = new PromptKeywordOptions("\n选择要删除的图块:");
                foreach (string uniqueFileName in fileNameToPathMap.Keys)
                {
                    pko.Keywords.Add(uniqueFileName);
                }
                pko.AllowNone = true;

                PromptResult pr = ed.GetKeywords(pko);
                if (pr.Status != PromptStatus.OK)
                    return;

                string selectedFile = null;
                if (fileNameToPathMap.TryGetValue(pr.StringResult, out string fullPath))
                {
                    selectedFile = fullPath;
                }
                else
                {
                    ed.WriteMessage($"\n找不到文件: {pr.StringResult}");
                    return;
                }

                PromptKeywordOptions confirmOptions = new PromptKeywordOptions($"\n确定要删除 {Path.GetFileName(selectedFile)} 吗? (Y/N):");
                confirmOptions.Keywords.Add("Y");
                confirmOptions.Keywords.Add("N");
                confirmOptions.AllowNone = false;
                
                PromptResult confirmResult = ed.GetKeywords(confirmOptions);
                if (confirmResult.Status != PromptStatus.OK || confirmResult.StringResult.ToUpper() != "Y")
                {
                    ed.WriteMessage("\n取消删除操作");
                    return;
                }

                LibraryManagerService.DeleteFileFromLibrary(selectedFile);
                ed.WriteMessage($"\n图块 {Path.GetFileName(selectedFile)} 已删除！");
            }
            catch (System.Exception ex)
            {
                AcApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\n删除图块错误: {ex.Message}");
            }
        }

        // XHP - 帮助
        [CommandMethod("XHP", CommandFlags.Session)]
        public void ShowHelp()
        {
            ShowHelpDialog();
        }

        // 显示帮助对话框
        private void ShowHelpDialog()
        {
            try
            {
                Views.HelpDialog dialog = new Views.HelpDialog();
                dialog.ShowDialog();
            }
            catch (System.Exception ex)
            {
                // Session 模式下可能没有打开的图纸
                AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage($"\n打开帮助对话框错误: {ex.Message}");
            }
        }

        // 从当前CAD图形选择对象添加到图库
        [CommandMethod("XCCAD_ADDFROMCAD", CommandFlags.Modal)]
        public void AddFromCADToLibrary()
        {
            try
            {
                Document doc = AcApp.DocumentManager.MdiActiveDocument;
                Database db = doc.Database;
                Editor editor = doc.Editor;

                // 获取目标分类路径（从全局变量）
                string destFolder = !string.IsNullOrEmpty(App.AddToLibraryCategoryPath) 
                    ? App.AddToLibraryCategoryPath 
                    : App.LibraryPath;
                // 重置全局变量
                App.AddToLibraryCategoryPath = null;

                // 提示用户选择图形对象
                PromptSelectionOptions pso = new PromptSelectionOptions();
                pso.MessageForAdding = "\n选择要添加到图库的图形对象:";
                PromptSelectionResult psr = editor.GetSelection(pso);

                if (psr.Status != PromptStatus.OK)
                {
                    editor.WriteMessage("\n未选择任何对象！");
                    return;
                }

                // 提示用户输入图块名称
                PromptStringOptions stringOptions = new PromptStringOptions("\n请输入图块名称:");
                stringOptions.AllowSpaces = false;
                PromptResult pr = editor.GetString(stringOptions);

                if (pr.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(pr.StringResult))
                {
                    editor.WriteMessage("\n图块名称不能为空！");
                    return;
                }

                string blockName = pr.StringResult;

                // 验证文件名是否有效
                if (blockName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    editor.WriteMessage("\n图块名称包含无效字符！");
                    return;
                }

                // 创建临时DWG文件来保存选择的对象
                string tempFileName = Path.Combine(Path.GetTempPath(), $"{blockName}_{Guid.NewGuid()}.dwg");
                string destFileName = Path.Combine(destFolder, $"{blockName}.dwg");

                // 检查文件是否已存在
                if (File.Exists(destFileName))
                {
                    PromptKeywordOptions confirmOptions = new PromptKeywordOptions($"\n文件 {blockName}.dwg 已存在，是否覆盖？(Y/N):");
                    confirmOptions.Keywords.Add("Y");
                    confirmOptions.Keywords.Add("N");
                    confirmOptions.AllowNone = false;

                    PromptResult confirmResult = editor.GetKeywords(confirmOptions);
                    if (confirmResult.Status != PromptStatus.OK || confirmResult.StringResult.ToUpper() != "Y")
                    {
                        editor.WriteMessage("\n取消添加操作");
                        return;
                    }
                }

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    // 将SelectionSet转换为ObjectIdCollection
                    ObjectIdCollection ids = new ObjectIdCollection();
                    foreach (SelectedObject so in psr.Value)
                    {
                        ids.Add(so.ObjectId);
                    }

                    // 创建新的数据库并复制选择的对象
                    using (Database tempDb = new Database(true, true))
                    {
                        // 使用Wblock复制选择的对象到新数据库
                        db.Wblock(tempDb, ids, Point3d.Origin, DuplicateRecordCloning.Replace);

                        // 保存临时文件
                        tempDb.SaveAs(tempFileName, DwgVersion.Current);
                    }

                    // 复制到图库文件夹
                    File.Copy(tempFileName, destFileName, true);

                    // 删除临时文件
                    File.Delete(tempFileName);

                    tr.Commit();
                }

                editor.WriteMessage($"\n图形 \"{blockName}\" 已成功添加到图库！");
            }
            catch (System.Exception ex)
            {
                AcApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\n从CAD图形添加到图库错误: {ex.Message}");
            }
        }

        // 将当前图块添加到图库
        [CommandMethod("XAD", CommandFlags.Modal)]
        public void AddCurrentBlockToLibrary()
        {
            try
            {
                Document doc = AcApp.DocumentManager.MdiActiveDocument;
                Database db = doc.Database;

                // 获取当前选择的图块
                PromptEntityOptions peo = new PromptEntityOptions("\n选择要添加到图库的图块:");
                peo.SetRejectMessage("请选择一个图块引用！");
                peo.AddAllowedClass(typeof(BlockReference), true);
                PromptEntityResult per = doc.Editor.GetEntity(peo);

                if (per.Status != PromptStatus.OK)
                    return;

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockReference br = tr.GetObject(per.ObjectId, OpenMode.ForRead) as BlockReference;
                    BlockTableRecord btr = tr.GetObject(br.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;

                    // 创建临时DWG文件来保存图块
                string tempFileName = Path.Combine(Path.GetTempPath(), $"{btr.Name}_{Guid.NewGuid()}.dwg");
                // 使用当前选定的分类路径，如果没有选定则使用图库根目录
                string destFolder = string.IsNullOrEmpty(App.CurrentSelectedCategoryPath) ? App.LibraryPath : App.CurrentSelectedCategoryPath;
                string destFileName = Path.Combine(destFolder, $"{btr.Name}.dwg");

                    // 检查文件是否已存在
                    if (File.Exists(destFileName))
                    {
                        AcApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\n文件 {Path.GetFileName(destFileName)} 已存在，是否覆盖？(Y/N): ");
                        string response = Console.ReadLine();
                        if (response?.ToUpper() != "Y")
                            return;
                    }

                    // 创建新的数据库并复制图块
                    using (Database tempDb = new Database(true, true))
                    {
                        // 将图块复制到新数据库
                        ObjectIdCollection ids = new ObjectIdCollection();
                        ids.Add(btr.ObjectId);
                        db.Wblock(tempDb, ids, Point3d.Origin, DuplicateRecordCloning.Replace);
                        
                        // 保存临时文件
                        tempDb.SaveAs(tempFileName, DwgVersion.Current);
                    }

                    // 复制到图库文件夹
                    File.Copy(tempFileName, destFileName, true);
                    
                    // 删除临时文件
                    File.Delete(tempFileName);

                    tr.Commit();
                }

                AcApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\n已将当前图块添加到图库！");
            }
            catch (System.Exception ex)
            {
                AcApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\n添加当前图块到图库错误: {ex.Message}");
            }
        }

        // 加载菜单栏
        [CommandMethod("LXT", CommandFlags.Modal)]
        public void LoadToolbar()
        {
            try
            {
                Editor ed = AcApp.DocumentManager.MdiActiveDocument.Editor;
                ed.WriteMessage("\n正在加载小辰CAD工具箱菜单栏...");
                
                // 调用CreateMenu方法来加载菜单栏
                CreateMenu();
            }
            catch (System.Exception ex)
            {
                AcApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\n加载菜单栏错误: {ex.Message}");
            }
        }

        // 卸载菜单栏
        [CommandMethod("UXT", CommandFlags.Modal)]
        public void UnloadToolbar()
        {
            try
            {
                Editor ed = AcApp.DocumentManager.MdiActiveDocument.Editor;
                ed.WriteMessage("\n正在卸载小辰CAD工具箱菜单栏...");
                
                // 使用-menucmd命令卸载菜单
                ed.Command("menucmd", "P11=-");
                
                ed.WriteMessage("\n小辰CAD工具箱菜单栏已成功卸载！");
            }
            catch (System.Exception ex)
            {
                AcApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\n卸载菜单栏错误: {ex.Message}");
            }
        }

        // 创建菜单栏（XCMENU命令）
        [CommandMethod("XCMENU", CommandFlags.Modal)]
        public void CreateMenu()
        {
            Editor ed = AcApp.DocumentManager.MdiActiveDocument.Editor;
            
            try
            {
                ed.WriteMessage("\n正在创建小辰CAD工具箱菜单...");
                
                string tempDir = System.IO.Path.GetTempPath();
                string mnuFile = System.IO.Path.Combine(tempDir, "XcTools.mnu").Replace("\\", "/");
                
                System.IO.File.WriteAllText(mnuFile, @"***MENUGROUP=XCTOOLS
***POP11
**MENU_XCTOOLS
[小辰CAD工具箱]
[图库管理]^C^C_XTK
[-]
[连续插入]^C^C_XCM
[选图入库]^C^C_XCAD
[-]
[搜索图块]^C^C_XCFD
[刷新缩略图]^C^C_XCRF
[新建分类]^C^C_XCF
[重命名]^C^C_XCREN
[删除]^C^C_XCDEL
[-]
[帮助]^C^C_XHP
[命令列表]^C^C_XCH
");

                bool menuLoaded = false;

                try
                {
                    try
                    {
                        ed.Command("MENUUNLOAD", "XCTOOLS");
                    }
                    catch { }

                    ed.Command("MENULOAD", mnuFile);
                    menuLoaded = true;

                    TrySetMenuPosition(ed, "XCTOOLS", "MENU_XCTOOLS");
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\n菜单加载命令执行失败: {ex.Message}");
                    ed.WriteMessage("\n尝试使用 LISP 方式...");
                    try
                    {
                        AcApp.DocumentManager.MdiActiveDocument.SendStringToExecute("(command \"_MENUUNLOAD\" \"XCTOOLS\") ", true, false, false);
                        System.Threading.Thread.Sleep(100);
                        AcApp.DocumentManager.MdiActiveDocument.SendStringToExecute($"(command \"_MENULOAD\" \"{mnuFile}\") ", true, false, false);
                        System.Threading.Thread.Sleep(100);
                        menuLoaded = true;

                        TrySetMenuPositionLisp("XCTOOLS", "MENU_XCTOOLS");
                    }
                    catch (System.Exception ex2)
                    {
                        ed.WriteMessage($"\n菜单加载失败: {ex2.Message}");
                    }
                }
                finally
                {
                    System.Threading.Tasks.Task.Delay(2000).ContinueWith(t =>
                    {
                        try { System.IO.File.Delete(mnuFile); } catch { }
                    });
                }

                if (menuLoaded)
                {
                    ed.WriteMessage("\n小辰CAD工具箱菜单已成功创建！");
                    ed.WriteMessage("\n菜单已添加到自定义组 XCTOOLS");
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                ed.WriteMessage($"\n创建菜单错误: {ex.ErrorStatus} - {ex.Message}");
                ed.WriteMessage("\n请直接使用以下命令：");
                ed.WriteMessage("\n  XTK - 打开图库管理");
                ed.WriteMessage("  XCM - 连续插入");
                ed.WriteMessage("  XCAD - 选图入库");
                ed.WriteMessage("  XHP - 帮助");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n创建菜单错误: {ex.Message}");
            }
        }

        private void TrySetMenuPosition(Editor ed, string menuGroup, string menuName)
        {
            try
            {
                ed.Command("-MENUCmd", $"P11={menuGroup}.{menuName}");
            }
            catch
            {
                try
                {
                    ed.Command("MENUCmd", $"P11={menuGroup}.{menuName}");
                }
                catch
                {
                }
            }
        }

        private void TrySetMenuPositionLisp(string menuGroup, string menuName)
        {
            try
            {
                AcApp.DocumentManager.MdiActiveDocument.SendStringToExecute($"-menucmd P11={menuGroup}.{menuName} ", true, false, false);
            }
            catch
            {
                try
                {
                    AcApp.DocumentManager.MdiActiveDocument.SendStringToExecute($"menucmd P11={menuGroup}.{menuName} ", true, false, false);
                }
                catch
                {
                }
            }
        }

        [CommandMethod("XLOADCUIX", CommandFlags.Modal)]
        public void LoadCuixFile()
        {
            try
            {
                Editor ed = AcApp.DocumentManager.MdiActiveDocument.Editor;
                ed.WriteMessage("\n正在加载 CUIX 文件...");
                
                Services.CuixManager.LoadCuix();
            }
            catch (System.Exception ex)
            {
                AcApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\n加载 CUIX 文件错误: {ex.Message}");
            }
        }

        [CommandMethod("XUNLOADCUIX", CommandFlags.Modal)]
        public void UnloadCuixFile()
        {
            try
            {
                Editor ed = AcApp.DocumentManager.MdiActiveDocument.Editor;
                ed.WriteMessage("\n正在卸载 CUIX 文件...");
                
                Services.CuixManager.UnloadCuix();
            }
            catch (System.Exception ex)
            {
                AcApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\n卸载 CUIX 文件错误: {ex.Message}");
            }
        }

        [CommandMethod("XLOADUI", CommandFlags.Modal)]
        public void LoadFullInterface()
        {
            try
            {
                Editor ed = AcApp.DocumentManager.MdiActiveDocument.Editor;
                ed.WriteMessage("\n正在加载菜单栏界面...");

                // 手动命令强制重新走一遍 CUILOAD（即使启动流程已注册过）
                Services.CuixManager.LoadCuix();

                if (Services.CuixManager.IsMenuLoaded)
                {
                    ed.WriteMessage("\n「小辰CAD工具箱」菜单栏加载成功，可在文件/编辑/视图...右侧找到。");
                }
                else
                {
                    ed.WriteMessage("\n菜单栏加载提示：请直接使用命令 XTK、XCM、XAD 等（XCH 查看全部命令）。");
                }
            }
            catch (System.Exception ex)
            {
                AcApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\n加载界面错误: {ex.Message}");
            }
        }

        [CommandMethod("XRIBBON", CommandFlags.Modal)]
        public void LoadRibbonCommand()
        {
            try
            {
                Editor ed = AcApp.DocumentManager.MdiActiveDocument.Editor;
                Services.RibbonManager.LoadRibbon();
                ed.WriteMessage(Services.RibbonManager.IsRibbonLoaded
                    ? "\nRibbon 已加载：可在选项卡栏找到「小辰CAD工具箱」。"
                    : "\nRibbon 未能加载，请直接使用命令 XTK、XCM 等。");
            }
            catch (System.Exception ex)
            {
                AcApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\n加载 Ribbon 错误: {ex.Message}");
            }
        }

        [CommandMethod("URIBBON", CommandFlags.Modal)]
        public void UnloadRibbonCommand()
        {
            try
            {
                Services.RibbonManager.UnloadRibbon();
            }
            catch (System.Exception ex)
            {
                AcApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\n卸载 Ribbon 错误: {ex.Message}");
            }
        }

        [CommandMethod("XSETLIBPATH", CommandFlags.Modal)]
        public void SetLibraryPath()
        {
            try
            {
                Editor ed = AcApp.DocumentManager.MdiActiveDocument.Editor;
                ed.WriteMessage("\n当前图库路径: " + App.LibraryPath);
                
                PromptKeywordOptions pko = new PromptKeywordOptions("\n选择操作: [S]设置新路径 / [R]重置为默认 / [C]查看当前");
                pko.Keywords.Add("S");
                pko.Keywords.Add("R");
                pko.Keywords.Add("C");
                pko.AllowNone = false;
                
                PromptResult pr = ed.GetKeywords(pko);
                
                if (pr.Status != PromptStatus.OK)
                    return;
                
                string choice = pr.StringResult.ToUpper();
                
                switch (choice)
                {
                    case "S":
                        using (var folderDialog = new System.Windows.Forms.FolderBrowserDialog())
                        {
                            folderDialog.Description = "选择图库根目录（更换后不会自动创建默认分类）";
                            folderDialog.SelectedPath = App.LibraryPath;
                            
                            if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                            {
                                string newPath = folderDialog.SelectedPath;
                                LibraryManagerService.SetLibraryPath(newPath);
                                App.SetLibraryPath(newPath);
                                ed.WriteMessage($"\n图库路径已更换为: {newPath}");
                                ed.WriteMessage("\n注意：更换目录后不会自动创建默认分类结构。");
                            }
                        }
                        break;
                        
                    case "R":
                        LibraryManagerService.ResetLibraryPath();
                        App.SetLibraryPath(LibraryManagerService.GetLibraryPath());
                        LibraryManagerService.EnsureDefaultCategoriesExists();
                        ed.WriteMessage($"\n图库路径已重置为默认: {App.LibraryPath}");
                        ed.WriteMessage("\n默认分类结构已创建。");
                        break;
                        
                    case "C":
                        ed.WriteMessage($"\n当前图库路径: {App.LibraryPath}");
                        ed.WriteMessage($"\n默认路径: {LibraryManagerService.GetDefaultLibraryPath()}");
                        break;
                }
            }
            catch (System.Exception ex)
            {
                AcApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\n设置图库路径错误: {ex.Message}");
            }
        }

        [CommandMethod("XLIBPATH", CommandFlags.Modal)]
        public void ShowLibraryPath()
        {
            try
            {
                Editor ed = AcApp.DocumentManager.MdiActiveDocument.Editor;
                ed.WriteMessage($"\n当前图库路径: {App.LibraryPath}");
                ed.WriteMessage($"\n默认图库路径: {LibraryManagerService.GetDefaultLibraryPath()}");
                
                if (App.LibraryPath != LibraryManagerService.GetDefaultLibraryPath())
                {
                    ed.WriteMessage("\n提示: 图库路径已自定义，使用 XSETLIBPATH 命令可以修改或重置。");
                }
            }
            catch (System.Exception ex)
            {
                AcApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\n显示图库路径错误: {ex.Message}");
            }
        }
    }
}