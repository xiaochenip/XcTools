using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Autodesk.AutoCAD.Runtime;

[assembly: AssemblyTitle("小辰CAD工具箱")]
[assembly: AssemblyDescription("小辰CAD工具箱 - 提供图库管理、图块插入、快捷操作等 CAD 绘图效率提升功能")]
[assembly: AssemblyConfiguration("Release")]
[assembly: AssemblyCompany("小辰设计")]
[assembly: AssemblyProduct("小辰CAD工具箱")]
[assembly: AssemblyCopyright("Copyright © 小辰设计 2026")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

[assembly: ComVisible(false)]
[assembly: Guid("00000000-0000-0000-0000-000000000000")]

[assembly: AssemblyVersion("1.0.1.0")]
[assembly: AssemblyFileVersion("1.0.1.0")]
[assembly: AssemblyInformationalVersion("1.0.1")]

[assembly: ExtensionApplication(typeof(XcTools.App))]
[assembly: CommandClass(typeof(XcTools.Commands.Commands))]