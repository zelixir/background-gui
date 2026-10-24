using System;
using System.Text;

internal static class Program
{
    static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }
        DpiAware();
        try
        {
            return Cmds.Run(args);
        }
        catch (UsageError ue)
        {
            Console.Error.WriteLine(Json.Value(new JObj { ["error"] = new JStr(ue.Message), ["usage"] = new JStr(Cmds.UsageAll) }));
            return 2;
        }
        catch (ToolError te)
        {
            Console.Error.WriteLine(Json.Value(te.Payload));
            return 3;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(Json.Value(new JObj { ["error"] = new JStr(ex.Message) }));
            return 3;
        }
    }

    static void DpiAware()
    {
        try { if (Win32.SetProcessDpiAwarenessContext(new IntPtr(-4))) return; } catch { }
        try { Win32.SetProcessDPIAware(); } catch { }
    }
}
