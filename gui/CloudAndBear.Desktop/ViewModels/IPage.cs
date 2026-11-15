namespace CloudAndBear.Desktop.ViewModels;

// stranica koja zna kad je otvorena: tek tada osvežava podatke (spoljni programi se ne pokreću za skrivene stranice)
public interface IPage
{
    void OnShown();

    void OnHidden();
}
