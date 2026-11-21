using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Xml;

namespace QingQi
{
    // Read registered logon/boot tasks; never register, enable, disable or run a task.
    internal static class ScheduledStartup
    {
        internal static void Read(List<StartupItem> items)
        {
            object service = null, root = null;
            try
            {
                service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));
                ((dynamic)service).Connect(); root = ((dynamic)service).GetFolder("\\"); ReadFolder(root, items);
            }
            catch (COMException) { }
            catch (UnauthorizedAccessException) { }
            finally { Release(root); Release(service); }
        }
        private static void ReadFolder(object folder, List<StartupItem> items)
        {
            object tasks = null, children = null;
            try
            {
                tasks = ((dynamic)folder).GetTasks(1);
                for (int i = 1; i <= (int)((dynamic)tasks).Count; i++)
                {
                    object task = null;
                    try
                    {
                        task = ((dynamic)tasks).Item(i); var xml = new XmlDocument { XmlResolver = null }; xml.LoadXml((string)((dynamic)task).Xml);
                        var ns = new XmlNamespaceManager(xml.NameTable); ns.AddNamespace("t", xml.DocumentElement.NamespaceURI);
                        if (xml.SelectSingleNode("/t:Task/t:Triggers/t:LogonTrigger | /t:Task/t:Triggers/t:BootTrigger", ns) == null) continue;
                        foreach (XmlNode action in xml.SelectNodes("/t:Task/t:Actions/t:Exec", ns))
                        {
                            XmlNode command = action.SelectSingleNode("t:Command", ns), args = action.SelectSingleNode("t:Arguments", ns), working = action.SelectSingleNode("t:WorkingDirectory", ns);
                            if (command == null) continue;
                            StartupItem item = StartupReader.ParseCommand((string)((dynamic)task).Name, "\"" + command.InnerText.Trim('"') + "\" " + (args == null ? "" : args.InnerText), "登录/开机计划任务 · " + (string)((dynamic)task).Path);
                            if (item == null) continue;
                            if (working != null && !String.IsNullOrWhiteSpace(working.InnerText)) item.WorkingDirectory = Environment.ExpandEnvironmentVariables(working.InnerText);
                            item.State = (bool)((dynamic)task).Enabled ? "已注册" : "计划任务已禁用"; items.Add(item);
                        }
                    }
                    catch (COMException) { }
                    catch (XmlException) { }
                    finally { Release(task); }
                }
                children = ((dynamic)folder).GetFolders(0);
                for (int i = 1; i <= (int)((dynamic)children).Count; i++)
                { object child = null; try { child = ((dynamic)children).Item(i); ReadFolder(child, items); } finally { Release(child); } }
            }
            catch (COMException) { }
            finally { Release(children); Release(tasks); }
        }
        private static void Release(object value) { if (value != null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
    }
}
