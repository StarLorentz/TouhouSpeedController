// Construct the production form without showing it or starting any backend/game.
// Build with /main:NewGamesUiChecks and the regular application sources/references.
using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using TouhouSpeedController;

internal static class NewGamesUiChecks
{
    private static readonly BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Instance;
    private static T Control<T>(MainForm f,string name) where T : class { return f.GetType().GetField(name,Flags).GetValue(f) as T; }
    private static int checks;
    private static void Check(bool ok,string text) { if(!ok) throw new Exception(text); checks++; Console.WriteLine("PASS "+text); }
    [STAThread] private static int Main()
    {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        using(MainForm form=new MainForm()) {
            MethodInfo configure=typeof(MainForm).GetMethod("ConfigurePracticeOptions",Flags);
            MethodInfo enabled=typeof(MainForm).GetMethod("SetPracticeInputsEnabled",Flags,null,new Type[]{typeof(bool),typeof(PracticeOptions),typeof(bool)},null);
            MethodInfo show=typeof(MainForm).GetMethod("ShowPracticeSnapshot",Flags);
            foreach(string name in new string[]{"th08","th09","th12","th12c","th13","th13c","th16","th08","th16"}) {
                configure.Invoke(form,new object[]{name}); PracticeOptions o=PracticeMemoryAccess.Options(name);
                enabled.Invoke(form,new object[]{true,o,PowerSyncProfile.Supports(name)});
                PracticeSnapshot snapshot=new PracticeSnapshot { Lives=o.LivesMax, Bombs=o.BombsMax, BombFragments=o.FragmentsMax, Power=o.PowerMax };
                show.Invoke(form,new object[]{snapshot});
                Check(Control<NumericUpDown>(form,"practicePower").Value==o.PowerMax/100m,name+" display/range and switching");
                Check(Control<NumericUpDown>(form,"practiceBombFragments").Enabled==o.HasFragments&&Control<Button>(form,"applyPracticeBombFragments").Enabled==o.HasFragments,name+" fragments availability");
                Check(Control<NumericUpDown>(form,"practiceBombs").Enabled==o.HasBombs&&Control<Button>(form,"applyPracticeBombs").Enabled==o.HasBombs,name+" Bomb availability");
                Check(Control<CheckBox>(form,"lockPower").Enabled&&Control<CheckBox>(form,"infiniteLives").Enabled,name+" locks available");
                foreach(string field in new string[]{"applyPracticeBombs","applyPracticePower","infiniteBombs","lockPower"}) {
                    System.Windows.Forms.Control c=Control<System.Windows.Forms.Control>(form,field);
                    Check(c.Right<=c.Parent.ClientSize.Width&&c.Bottom<=c.Parent.ClientSize.Height,name+" "+field+" fits panel");
                }
                Check(Control<Button>(form,"applyPracticeBombs").Left==Control<Button>(form,"applyPracticePower").Left,name+" application buttons aligned");
            }
        }
        Console.WriteLine("PASS total="+checks); return 0;
    }
}
