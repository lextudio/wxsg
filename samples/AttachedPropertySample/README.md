# AttachedPropertySample

This small sample demonstrates an unqualified attached-property attribute usage that the WXSG
semantic binder now resolves correctly.

- XAML: `Window1.xaml` shows `<local:AttachedPropertyControl ShowAlternation="True" />` (no owner qualifier).
- Control: `AttachedPropertyControl.cs` declares the attached property `ShowAlternation`.

Use this sample as a minimal reproduction for the semantic binder fix implemented in
`WpfSemanticBinder.BindPropertyAssignment`.
