using System;
using System.Windows;
using System.Windows.Controls;

namespace DefaultStyleKeySubclassSample;

public class CustomTextBox : TextBox
{
    static CustomTextBox()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(CustomTextBox),
            new FrameworkPropertyMetadata(typeof(CustomTextBox)));
    }

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        SampleLog.Write($"[OnInitialized] InstanceType={GetType().FullName} IsInitialized={IsInitialized}");
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        SampleLog.Write(
            $"[OnApplyTemplate] InstanceType={GetType().FullName} Template={(Template != null ? "non-null" : "NULL")}");
    }
}
