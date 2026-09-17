using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;

namespace XcTools.Services
{
    /// <summary>
    /// 「小辰CAD工具箱」经典菜单栏注入（AutoCAD 2021-2026 兼容）。
    ///
    /// 三层保险（只要任一层成功，菜单栏就会显示）：
    ///   1. 生成标准 Partial CUIx 包（XcTools_Menu.cuix = ZIP 容器），
    ///      发 CUILOAD 注册到 acad.CUIX 的 PartialMenuFile 列表；
    ///      AutoCAD 启动时自动加载已登记的部分菜单。
    ///   2. 枚举 acad.CUIX 已注册的部分菜单路径作为可靠判据，
    ///      避免误判（"本地文件有定义" != "AutoCAD 进程已加载"）。
    ///   3. CUIX 注册仍失败时，降级用 COM (AutoCAD.Interop) 直接把菜单
    ///      加进菜单栏 Classic PopupMenus 集合（源泉设计的经典方案）。
    ///
    /// 注意：裸 .cui（单文件 XML）会被 AutoCAD 收编为 .cuix 时丢弃内容，
    /// 必须直接产出完整 .cuix 包（实测结论，勿回退）。
    /// </summary>
    public static class CuixManager
    {
        private const string MenuGroupName = "XCTOOLS_MENU";
        private const string MenuDisplayName = "小辰CAD工具箱";
        private const string CuixFileName = "XcTools_Menu.cuix";
        private const string PopMenuUid = "PM_XCTOOLS_MAIN";

        private static bool _isMenuLoaded;
        private static bool _verifyHooked;
        private static int _verifyTicks;
        public static bool IsMenuLoaded => _isMenuLoaded;

        /// <summary>菜单定义：（显示名, 命令, 该项之后是否加分隔线）</summary>
        private static readonly (string Name, string Command, bool SepAfter)[] Items =
        {
            ("图库管理",   "XTK",   false),
            ("命令列表",   "XCH",   false),
            ("帮助",       "XHP",   false),
            ("卸载",       "XCTUNSET",   false),
        };

        /// <summary>
        /// 菜单宏模板（CUIX 的 MenuMacro.Command 节点 & COM AddMenuItem 第 3 参通用）。
        /// 
        /// 采用极简格式：纯命令名 + 尾部空格（回车执行）。
        /// 说明：官方标准是 ^C^C_XXX（连按两次取消 + 国际化命令前缀），但
        /// AutoCAD 经典菜单栏若存在"悬挂 PopMenu"，点击时会把 ^C^C_XXX 当成
        /// 命令名原样送到命令行报「未知命令 "^C^C_XXX"」。为保证任何情况下
        /// 点击都等同于直接键入命令，这里简化为纯命令名，与用户手动输入一致。
        /// </summary>
        private static string MacroFor(string cmd)
            => $"{cmd} ";

        // --------------------------------------------------------------

        public static void LoadDefaultInterface()
        {
            try { LoadMenu(firstTimeOnly: true); }
            catch (Exception ex)
            {
                App.Log($"CuixManager.LoadDefaultInterface ERROR: {ex.Message}");
                Editor ed = AcApp.DocumentManager.MdiActiveDocument?.Editor;
                ed?.WriteMessage($"\n加载界面失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 确保菜单包存在并按需加载，且最终保证菜单栏可见。
        /// 启动自动流程：先查宿主进程内存里是否已有同名菜单组（AutoCAD 启动会自动
        /// 加载已登记的部分菜单）——已在内存则完全静默跳过，绝不再发 CUILOAD，
        /// 否则会弹「无法加载自定义文件：该自定义组的名称已存在」。
        /// 组不在内存：先 CUIUNLOAD 清旧宏残留（幂等），再 CUILOAD 最新包；
        /// CUILOAD 成功后 AutoCAD 自动把文件登记为部分菜单（partial），
        /// 之后每次启动宿主自动加载，走上面的静默分支。
        ///
        /// 可见性保证（缺一不可，否则"组加载了但菜单栏什么都没有"）：
        ///   1. MENUBAR=1：草图与注释等工作空间默认隐藏经典菜单栏；
        ///   2. EnsureInMenuBar：把弹出菜单插进菜单栏（幂等）；
        ///   3. CUILOAD 是异步的：挂一次性 Idle 校验，等组真正进内存后
        ///      再插菜单栏；CUILOAD 始终没完成才降级 COM 兜底。
        /// </summary>
        public static void LoadMenu(bool firstTimeOnly)
        {
            App.Log($"CuixManager.LoadMenu(firstTimeOnly={firstTimeOnly}) START");
            try
            {
                string cuixPath = EnsureMenuPackage();
                Document doc = AcApp.DocumentManager.MdiActiveDocument;

                bool inMemory = MenuGroupInMemory();
                App.Log($"CuixManager: menu group '{MenuGroupName}' in memory = {inMemory}");

                if (doc == null)
                {
                    App.Log("CuixManager.LoadMenu: no document — deferring");
                    _isMenuLoaded = inMemory;
                    return;
                }

                // 经典菜单栏默认隐藏（MENUBAR=0）时不显示任何菜单项，先强制打开
                ShowMenuBar();

                // ── 预清理：先清本插件的所有旧菜单组（CUI 登记 + COM 残留），避免组名冲突
                // 宿主 CUI 机制会把 CUILOAD 过的组登记为"部分菜单"，但如果包结构有差异，
                // PopMenuRoot 不会挂上去（Menus=0），后续弹出菜单插入或 COM 兜底
                // 都会因"同组名"而失败。每次启动都先完整清除再重建，是最稳妥的做法。
                try
                {
                    dynamic acad = AcApp.AcadApplication;
                    var groupsToUnload = new List<string>();
                    for (int i = acad.MenuGroups.Count - 1; i >= 0; i--)
                    {
                        string nm = null;
                        try { nm = (string)acad.MenuGroups.Item(i).Name; } catch { }
                        if (nm == null) continue;
                        // 只卸本插件明确注册过的两个组名，绝不碰其他插件的
                        // （YQARCH、源泉设计、批打印等组名均不与以下两者重合，安全）
                        if (nm.Equals(MenuGroupName, StringComparison.OrdinalIgnoreCase)
                            || nm.Equals("XCTOOLS", StringComparison.OrdinalIgnoreCase))
                        {
                            groupsToUnload.Add(nm);
                        }
                    }
                    if (groupsToUnload.Count > 0)
                    {
                        foreach (string gnm in groupsToUnload)
                            SendScript(doc, $"FILEDIA 0 _CUIUNLOAD {gnm} _FILEDIA {GetFileDia()} ");
                        App.Log($"CuixManager: 已发送卸载旧菜单组的脚本: [{string.Join(", ", groupsToUnload)}]");
                    }
                }
                catch { }

                // ═══════════════════════════════════════════════════════════
                // 菜单加载策略：只用 COM (AcadMenuGroups.Add + InsertInMenuBar)
                // 不再依赖 CUILOAD / CUIx 宏绑定。宿主 CUI 部分菜单机制
                // 会持久化 PopMenu 引用，多次迭代后容易形成"悬挂项"，
                // 点击时报 未知命令"^C^C_XXX"；而 COM Classic Popup 菜单
                // 是进程内临时对象，退出即销毁，无残留无歧义。
                // ═══════════════════════════════════════════════════════════
                //
                //（pre-cleanup 已在上方执行过 CUIUNLOAD，宿主登记已清。）
                //
                // 注意：此处必须在 CUILOAD 之前执行 COM 注入，否则已登记的部分菜单
                // 会导致 acad.MenuGroups.Add 报 "该自定义组的名称已存在"。

                InjectClassicMenuViaCom();
                App.Log("CuixManager.LoadMenu: 菜单通过 COM 方式已注入菜单栏。");
                _isMenuLoaded = true;

                if (!firstTimeOnly)
                    doc.Editor.WriteMessage($"\n「{MenuDisplayName}」菜单已加载，可在菜单栏查看；输入 XCH 查看全部命令。");

                App.Log("CuixManager.LoadMenu COMPLETE");
            }
            catch (Exception ex)
            {
                App.Log($"CuixManager.LoadMenu ERROR: {ex.Message}\r\n{ex.StackTrace}");
                Editor ed = AcApp.DocumentManager.MdiActiveDocument?.Editor;
                ed?.WriteMessage("\n[提示] 菜单栏菜单未能加载，请使用命令 XTK、XCM、XAD 等调用各项功能（XCH 查看全部命令）。");
            }
        }

        public static void UnloadMenu()
        {
            try
            {
                Document doc = AcApp.DocumentManager.MdiActiveDocument;
                if (doc != null)
                {
                    SendScript(doc, $"FILEDIA 0 _CUIUNLOAD {MenuGroupName} _FILEDIA {GetFileDia()} ");
                }
                _isMenuLoaded = false;
                doc?.Editor.WriteMessage($"\n「{MenuDisplayName}」菜单已卸载。");
                App.Log("CuixManager.UnloadMenu COMPLETE");
            }
            catch (Exception ex)
            {
                App.Log($"CuixManager.UnloadMenu ERROR: {ex.Message}");
            }
        }

        public static void LoadCuix(string cuiPath = null)
            => LoadMenu(firstTimeOnly: false);
        public static void UnloadCuix() => UnloadMenu();

        // --------------------------------------------------------------

        private static string GetPluginDir()
        {
            // 插件根目录（.cuix 与 XcTools.lsp 同层，不在 net8/net48 子目录）
            return App.PluginDir;
        }

        private static string CuixPath() => Path.Combine(GetPluginDir(), CuixFileName);

        /// <summary>
        /// 每次启动都重建 .cuix 包：开销极小（十几个 XML 成员的 ZIP），
        /// 保证宏格式、菜单项与当前 DLL 代码严格一致，避免旧宏残留。
        /// </summary>
        private static string EnsureMenuPackage()
        {
            string path = CuixPath();
            BuildCuixPackage(path);
            App.Log($"CuixManager: CUIx 包已重建: {path}");
            return path;
        }

        /// <summary>
        /// 生成标准 .cuix（ZIP 容器）。文件结构以 AutoCAD 2026 官方 acad.CUIX /
        /// AutoCAD 自行收编生成的骨架为模板，逐成员一致。
        /// </summary>
        private static void BuildCuixPackage(string path)
        {
            const string xmlDecl = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n";
            const string ns = " xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\"";

            string headerCui =
                xmlDecl +
                $"<CustSection{ns}>\n" +
                "  <FileVersion MajorVersion=\"0\" MinorVersion=\"6\" IncrementalVersion=\"1\" UserVersion=\"1\" />\n" +
                "  <Header>\n" +
                "    <CommonConfiguration>\n" +
                "      <CommonItems>\n" +
                "        <ModifiedRev MajorVersion=\"25\" MinorVersion=\"1\" UserVersion=\"1\" />\n" +
                "      </CommonItems>\n" +
                "    </CommonConfiguration>\n" +
                "  </Header>\n" +
                "</CustSection>\n";

            // ---- MenuGroup.cui：组名 + 全部菜单宏 ----
            var mg = new StringBuilder();
            mg.Append(xmlDecl);
            mg.Append($"<MenuGroup{ns} Name=\"{MenuGroupName}\">\n");
            mg.Append("  <MacroGroup Name=\"XcToolsMacros\" Citizen=\"A\">\n");
            foreach (var (name, cmd, _) in Items)
            {
                mg.Append($"    <MenuMacro UID=\"ID_XC_{cmd}\">\n");
                mg.Append("      <Macro type=\"Any\">\n");
                mg.Append("        <Revision MajorVersion=\"16\" MinorVersion=\"2\" UserVersion=\"0\" />\n");
                mg.Append("        <ModifiedRev MajorVersion=\"17\" MinorVersion=\"2\" UserVersion=\"0\" />\n");
                mg.Append($"        <Name xlate=\"true\">{name}</Name>\n");
                mg.Append($"        <Command>{MacroFor(cmd)}</Command>\n");
                mg.Append("        <HelpString xlate=\"true\" />\n");
                mg.Append("      </Macro>\n");
                mg.Append("    </MenuMacro>\n");
            }
            mg.Append("  </MacroGroup>\n");
            mg.Append("</MenuGroup>\n");

            // ---- PopMenuRoot.cui：菜单栏弹出菜单 ----
            var pr = new StringBuilder();
            pr.Append(xmlDecl);
            pr.Append($"<PopMenuRoot{ns}>\n");
            pr.Append($"  <PopMenu hasDiesel=\"false\" UID=\"{PopMenuUid}\">\n");
            pr.Append("    <ModifiedRev MajorVersion=\"25\" MinorVersion=\"1\" UserVersion=\"0\" />\n");
            pr.Append("    <Alias>POP11</Alias>\n");
            pr.Append($"    <Name xlate=\"true\">{MenuDisplayName}</Name>\n");
            int i = 1;
            foreach (var (name, cmd, sepAfter) in Items)
            {
                pr.Append($"    <PopMenuItem IsSeparator=\"false\" hasDiesel=\"false\" UID=\"PMI_XC_{i:D3}\">\n");
                pr.Append("      <ModifiedRev MajorVersion=\"16\" MinorVersion=\"2\" UserVersion=\"0\" />\n");
                pr.Append($"      <NameRef xlate=\"true\">{name}</NameRef>\n");
                pr.Append("      <MenuItem>\n");
                pr.Append($"        <MacroRef MenuMacroID=\"ID_XC_{cmd}\" />\n");
                pr.Append("      </MenuItem>\n");
                pr.Append("    </PopMenuItem>\n");
                if (sepAfter)
                {
                    pr.Append("    <PopMenuItem IsSeparator=\"true\" hasDiesel=\"false\">\n");
                    pr.Append("      <ModifiedRev MajorVersion=\"16\" MinorVersion=\"2\" UserVersion=\"0\" />\n");
                    pr.Append("    </PopMenuItem>\n");
                }
                i++;
            }
            pr.Append("  </PopMenu>\n");
            pr.Append("</PopMenuRoot>\n");

            // ---- 空根存根（与 AutoCAD 收编骨架一致）----
            string[] stubRoots =
            {
                "AcceleratorRoot", "DigitizerButtonRoot", "DoubleClickRoot",
                "ImageMenuRoot", "MouseButtonRoot", "OverrideRoot",
                "QuickAccessToolbarRoot", "QuickPropertiesRoot",
                "RolloverTooltipRoot", "ScreenMenuRoot", "TabletMenuRoot",
                "ToolbarRoot", "ToolPanelRoot"
            };

            string contentTypes =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"cui\" ContentType=\"text/xml\" /><Default Extension=\"xml\" ContentType=\"text/xml\" /><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\" /></Types>";

            string rels =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Type=\"CUI\" Target=\"/Header.cui\" Id=\"RXCTOOLS01\" />" +
                "<Relationship Type=\"CUI\" Target=\"/WorkspaceRoot.cui\" Id=\"RXCTOOLS02\" />" +
                "<Relationship Type=\"CUI\" Target=\"/MenuGroup.cui\" Id=\"RXCTOOLS03\" />" +
                "<Relationship Type=\"CUI\" Target=\"/PopMenuRoot.cui\" Id=\"RXCTOOLS04\" />" +
                "<Relationship Type=\"CUI\" Target=\"/LSPFiles.cui\" Id=\"RXCTOOLS05\" />" +
                "<Relationship Type=\"CUI\" Target=\"/PanelSetRoot.cui\" Id=\"RXCTOOLS06\" />";
            foreach (string r in stubRoots)
                rels += $"<Relationship Type=\"CUI\" Target=\"/{r}.cui\" Id=\"RXCT_{r}\" />";
            rels += "</Relationships>";

            // ---- Menu_Package_Info.xml：包部件清单 ----
            // AutoCAD 2025+ 的 CUIx 加载器依此枚举并装载各部件；
            // 缺少该清单时 MenuGroup.cui 能加载（组存在）但 PopMenuRoot.cui
            // 不会挂到组上（Menus.Count=0），菜单宏也无法正确绑定。
            string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.ffffff+00:00");
            var mpi = new StringBuilder();
            mpi.Append(xmlDecl);
            mpi.Append("<MenuPackageParts>\n");
            mpi.Append($"  <PartData PartData_Name=\"/Header.cui\" PartData_Modified=\"{now}\" />\n");
            mpi.Append($"  <PartData PartData_Name=\"/MenuGroup.cui\" PartData_Modified=\"{now}\" />\n");
            mpi.Append($"  <PartData PartData_Name=\"/PopMenuRoot.cui\" PartData_Modified=\"{now}\" />\n");
            mpi.Append($"  <PartData PartData_Name=\"/WorkspaceRoot.cui\" PartData_Modified=\"{now}\" />\n");
            mpi.Append($"  <PartData PartData_Name=\"/LSPFiles.cui\" PartData_Modified=\"{now}\" />\n");
            mpi.Append($"  <PartData PartData_Name=\"/PanelSetRoot.cui\" PartData_Modified=\"{now}\" />\n");
            foreach (string r in stubRoots)
                mpi.Append($"  <PartData PartData_Name=\"/{r}.cui\" PartData_Modified=\"{now}\" />\n");
            mpi.Append($"  <PartData PartData_Name=\"/VirtualMNRRoot\" PartData_Modified=\"{now}\" />\n");
            mpi.Append($"  <PartData PartData_Name=\"/Menu_Package_Info.xml\" PartData_Modified=\"{now}\" />\n");
            mpi.Append("</MenuPackageParts>\n");

            string workspaceRoot =
                xmlDecl +
                $"<WorkspaceRoot{ns}>\n  <WorkspaceConfigRoot />\n</WorkspaceRoot>\n";

            string lspFiles = xmlDecl + $"<LSPFiles{ns} />\n";
            string panelSet = xmlDecl +
                $"<PanelSetRoot{ns}>\n  <ToolPanelSetRoot />\n</PanelSetRoot>\n";

            // ---- 写 ZIP ----
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                WriteEntry(zip, "[Content_Types].xml", contentTypes);
                WriteEntry(zip, "_rels/.rels", rels);
                WriteEntry(zip, "Header.cui", headerCui);
                WriteEntry(zip, "MenuGroup.cui", mg.ToString());
                WriteEntry(zip, "PopMenuRoot.cui", pr.ToString());
                WriteEntry(zip, "Menu_Package_Info.xml", mpi.ToString());
                WriteEntry(zip, "WorkspaceRoot.cui", workspaceRoot);
                WriteEntry(zip, "LSPFiles.cui", lspFiles);
                WriteEntry(zip, "PanelSetRoot.cui", panelSet);
                foreach (string r in stubRoots)
                    WriteEntry(zip, $"{r}.cui", xmlDecl + $"<{r}{ns} />\n");
            }
        }

        private static void WriteEntry(ZipArchive zip, string name, string content)
        {
            ZipArchiveEntry e = zip.CreateEntry(name, CompressionLevel.Optimal);
            using (var w = new StreamWriter(e.Open(), new UTF8Encoding(false)))
            {
                w.Write(content);
            }
        }

        // --------------------------------------------------------------

        /// <summary>
        /// 统一用 SendStringToExecute 发送脚本行（等同于用户在命令行键入）。
        ///
        /// AutoCAD 2026 注意事项（实测）：
        ///   - -CUILOAD / -CUIUNLOAD（连字符脚本版）已删除；
        ///   - Editor.Command 会给字符串参数自动加双引号 → "XXX" 被当成未知命令；
        ///   - 对话框版 CUILOAD/CUIUNLOAD 在 FILEDIA=0 时从命令队列读取参数。
        /// </summary>
        private static void SendScript(Document doc, string scriptLine)
        {
            if (doc == null) return;
            try
            {
                doc.SendStringToExecute(scriptLine, activate: true, wrapUpInactiveDoc: false, echoCommand: true);
                App.Log($"CuixManager.SendScript: {scriptLine.Trim()}");
            }
            catch (Exception ex)
            {
                App.Log($"CuixManager.SendScript ERROR: {ex.Message}");
                throw;
            }
        }

        private static short GetFileDia()
        {
            try
            {
                object v = AcApp.GetSystemVariable("FILEDIA");
                if (v == null) return 1;
                return Convert.ToInt16(v);
            }
            catch { return 1; }
        }

        // --------------------------------------------------------------
        // 菜单可靠性辅助：菜单栏可见性 / Classic 菜单探测 / COM 兜底
        // --------------------------------------------------------------

        /// <summary>打开经典菜单栏。草图与注释等工作空间默认 MENUBAR=0（隐藏），
        /// 组加载成功菜单栏上也不会有任何显示。</summary>
        private static void ShowMenuBar()
        {
            try
            {
                object v = AcApp.GetSystemVariable("MENUBAR");
                short cur = v == null ? (short)0 : Convert.ToInt16(v);
                App.Log($"CuixManager: MENUBAR={cur}");
                if (cur != 1)
                {
                    AcApp.SetSystemVariable("MENUBAR", 1);
                    App.Log("CuixManager: MENUBAR -> 1 (经典菜单栏已打开)");
                }
            }
            catch (Exception ex)
            {
                App.Log($"CuixManager.ShowMenuBar ERROR: {ex.Message}");
            }
        }

        /// <summary>
        /// 确保弹出菜单真的挂在菜单栏上（幂等）。
        /// 优先用已加载菜单组里解析出的 PopMenu 插入；
        /// 组里没有（包解析异常等）才整体 COM 注入。
        /// </summary>
        private static void EnsureInMenuBar()
        {
            try
            {
                if (ClassicMenuExists())
                {
                    App.Log("CuixManager.EnsureInMenuBar: 菜单已在菜单栏上，跳过插入。");
                    return;
                }

                dynamic acad = AcApp.AcadApplication;
                dynamic menubar = acad.MenuBar;

                dynamic mg = null;
                try { mg = acad.MenuGroups.Item(MenuGroupName); } catch { }

                if (mg != null)
                {
                    for (int i = 0; i < mg.Menus.Count; i++)
                    {
                        dynamic mm = mg.Menus.Item(i);
                        if (string.Equals((string)mm.Name, MenuDisplayName, StringComparison.Ordinal))
                        {
                            int idx = Math.Max(1, menubar.Count - 1);
                            mm.InsertInMenuBar(idx);
                            App.Log($"CuixManager: 弹出菜单「{MenuDisplayName}」已插入菜单栏位置 {idx}.");
                            return;
                        }
                    }
                }

                // 组不在内存或组里没有弹出菜单 → COM 全量兜底
                InjectClassicMenuViaCom();
                App.Log("CuixManager: COM fallback injected.");
            }
            catch (Exception ex)
            {
                App.Log($"CuixManager.EnsureInMenuBar ERROR: {ex.Message}");
            }
        }

        /// <summary>
        /// CUILOAD 经 SendStringToExecute 异步执行，挂一次性 Idle 校验：
        /// 每次空闲检查一次，组进内存后立即 EnsureInMenuBar；
        /// 超过 20 次仍未加载（CUILOAD 失败）才降级 COM 兜底。
        /// </summary>
        private static void BeginDeferredMenuBarVerify()
        {
            if (_verifyHooked) return;
            _verifyHooked = true;
            _verifyTicks = 0;
            AcApp.Idle += OnVerifyIdle;
            App.Log("CuixManager: deferred menu-bar verify hooked (Idle).");
        }

        private static void EndDeferredVerify()
        {
            if (!_verifyHooked) return;
            _verifyHooked = false;
            AcApp.Idle -= OnVerifyIdle;
        }

        private static void OnVerifyIdle(object sender, EventArgs e)
        {
            try
            {
                if (ClassicMenuExists() || EnsureInMenuBarWhenGroupLoaded())
                {
                    EndDeferredVerify();
                    return;
                }
                if (++_verifyTicks >= 20)
                {
                    EndDeferredVerify();
                    InjectClassicMenuViaCom();
                    App.Log("CuixManager: CUILOAD 未完成，COM fallback injected (deferred).");
                }
            }
            catch (Exception ex)
            {
                App.Log($"CuixManager.OnVerifyIdle ERROR: {ex.Message}");
                EndDeferredVerify();
            }
        }

        /// <summary>组已进内存则补挂菜单栏并返回 true；否则 false（继续等）。</summary>
        private static bool EnsureInMenuBarWhenGroupLoaded()
        {
            if (!MenuGroupInMemory()) return false;
            EnsureInMenuBar();
            return true;
        }

        /// <summary>探测宿主进程内存里是否已加载同名菜单组（最可靠判据）。</summary>
        private static bool MenuGroupInMemory()
        {
            try
            {
                dynamic acad = AcApp.AcadApplication;
                dynamic groups = acad.MenuGroups;
                int n = groups.Count;
                bool found = false;
                var infos = new List<string>();
                for (int i = 0; i < n; i++)
                {
                    try
                    {
                        dynamic g = groups.Item(i);
                        string nm = (string)g.Name;
                        int mc = g.Menus.Count;
                        infos.Add($"{nm}(Menus={mc})");
                        if (string.Equals(nm, MenuGroupName, StringComparison.OrdinalIgnoreCase))
                            found = true;
                    }
                    catch { }
                }
                App.Log($"CuixManager: 菜单组共 {n} 个 [{string.Join(" | ", infos)}]，目标存在={found}");
                return found;
            }
            catch { }
            return false;
        }

        /// <summary>探测经典菜单栏里是否已有同名菜单项（用于确认加载生效）。
        /// 注意：CUIX 加载后菜单栏项显示的是 PopMenu 的 Name（MenuDisplayName），
        /// 不是菜单组名，两个都要比对。</summary>
        private static bool ClassicMenuExists()
        {
            try
            {
                dynamic acad = AcApp.AcadApplication;
                dynamic menubar = acad.MenuBar;
                int n = menubar.Count;
                var names = new List<string>();
                bool found = false;
                for (int i = 1; i <= n; i++)
                {
                    string nm;
                    try { nm = (string)menubar.Item(i).Name; }
                    catch { nm = "<unreadable>"; }
                    names.Add(nm);
                    if (string.Equals(nm, MenuDisplayName, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(nm, MenuGroupName, StringComparison.OrdinalIgnoreCase))
                        found = true;
                }
                App.Log($"CuixManager: 菜单栏共 {n} 项 [{string.Join(" | ", names)}]，本插件菜单存在={found}");
                return found;
            }
            catch (System.Exception ex)
            {
                // COM 不可用时退化为假；后续流程仍会尝试 CUIX
                App.Log($"CuixManager.ClassicMenuExists ERROR: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// COM (IDispatch) 兜底：直接向 AutoCAD 经典菜单栏注入菜单项。
        /// AutoCAD.Interop 不随 SDK 单独分发，这里全用 dynamic 不依赖任何额外引用。
        /// </summary>
        private static void InjectClassicMenuViaCom()
        {
            dynamic acad = AcApp.AcadApplication;
            dynamic menubar = acad.MenuBar;
            dynamic menus = acad.MenuGroups;

            // ── 找/建菜单组（COM API 多版本兼容）────
            // 早期版本（含 net48）的 MenuGroups 集合不暴露名为 Add 的成员，
            // 因此依次尝试 Add / 反射 InvokeMember("Add") / 直接在 ACAD 组下建子菜单三种路径。
            dynamic mg = null;
            try { mg = menus.Item(MenuGroupName); } catch { mg = null; }
            if (mg == null)
            {
                try { mg = menus.Add(MenuGroupName); }
                catch
                {
                    try
                    {
                        mg = menus.GetType().InvokeMember(
                            "Add", System.Reflection.BindingFlags.InvokeMethod, null,
                            menus, new object[] { MenuGroupName });
                        App.Log("CuixManager.Inject: 菜单组通过反射 InvokeMember 已创建。");
                    }
                    catch
                    {
                        // 两种 Add 都失败 → 复用 ACAD 主菜单组，在其下新增一个弹出菜单。
                        // AutoCAD 官方 CUIX 本来就是 ACAD 主组挂载子弹出菜单，效果等同。
                        try { mg = menus.Item("ACAD"); } catch { mg = null; }
                        App.Log("CuixManager.Inject: 无法新建菜单组，降级挂到 ACAD 主菜单组。");
                    }
                }
            }

            // 若本插件同名菜单项已在菜单栏中，先删除再重插。
            // 注意：只移除名称严格匹配「小辰CAD工具箱 / XCTOOLS_MENU」的项，
            // 绝不碰其他插件（YQARCH、源泉设计、Express 等）的菜单栏项——
            // 之前"读不到子项就判悬挂并移除"的逻辑会误伤，已移除。
            try
            {
                for (int sweep = 0; sweep < 5; sweep++)
                {
                    bool removed = false;
                    int count;
                    try { count = menubar.Count; } catch { break; }
                    for (int i = count; i >= 1; i--)
                    {
                        dynamic entry = null;
                        string nm = null;
                        try { entry = menubar.Item(i); nm = (string)entry.Name; }
                        catch { continue; }
                        bool isTarget = (nm == MenuGroupName || nm == MenuDisplayName);
                        if (!isTarget) continue;
                        try
                        {
                            entry.RemoveFromMenuBar();
                            removed = true;
                            App.Log($"CuixManager: sweep{sweep} 已移除菜单栏第 {i} 位的同名菜单（Name={nm}）");
                            break; // 删后索引会变，下一轮再扫
                        }
                        catch (System.Exception rmEx)
                        {
                            App.Log($"CuixManager: sweep{sweep} 菜单栏第 {i} 位（Name={nm}）移除失败: {rmEx.Message}");
                        }
                    }
                    if (!removed) break;
                }
            }
            catch (System.Exception ex)
            {
                App.Log($"CuixManager: 清理菜单栏既有项异常: {ex.Message}");
            }

            dynamic popMenu = null;
            try
            {
                // 尝试复用已有的弹出菜单（组里同名）
                for (int i = 0; i < mg.Menus.Count; i++)
                {
                    dynamic mm = mg.Menus.Item(i);
                    if ((string)mm.Name == MenuDisplayName) { popMenu = mm; break; }
                }
            }
            catch { }

            if (popMenu == null)
            {
                try
                {
                    popMenu = mg.Menus.Add(MenuDisplayName);
                }
                catch
                {
                    popMenu = mg.Menus.GetType().InvokeMember(
                        "Add", System.Reflection.BindingFlags.InvokeMethod, null,
                        mg.Menus, new object[] { MenuDisplayName });
                }
                // 逐项加菜单项
                for (int i = 0; i < Items.Length; i++)
                {
                    var it = Items[i];
                    string macro = MacroFor(it.Command);
                    if (it.SepAfter && i < Items.Length - 1)
                    {
                        // 先加主项，再加分隔线
                        try { popMenu.AddMenuItem(i * 2, it.Name, macro); }
                        catch
                        {
                            popMenu.GetType().InvokeMember("AddMenuItem",
                                System.Reflection.BindingFlags.InvokeMethod, null, popMenu,
                                new object[] { i * 2, it.Name, macro });
                        }
                        try { popMenu.AddSeparator(i * 2 + 1); }
                        catch
                        {
                            popMenu.GetType().InvokeMember("AddSeparator",
                                System.Reflection.BindingFlags.InvokeMethod, null, popMenu,
                                new object[] { i * 2 + 1 });
                        }
                    }
                    else
                    {
                        try { popMenu.AddMenuItem(i, it.Name, macro); }
                        catch
                        {
                            popMenu.GetType().InvokeMember("AddMenuItem",
                                System.Reflection.BindingFlags.InvokeMethod, null, popMenu,
                                new object[] { i, it.Name, macro });
                        }
                    }
                }
            }

            // 插入菜单栏倒数第二位置（避免插入到"帮助"之后不显示）
            int idx = Math.Max(1, menubar.Count - 1);
            try { popMenu.InsertInMenuBar(idx); }
            catch
            {
                popMenu.GetType().InvokeMember("InsertInMenuBar",
                    System.Reflection.BindingFlags.InvokeMethod, null, popMenu,
                    new object[] { idx });
            }
        }
    }
}
