# DynamicResource Sample

This sample demonstrates WXSG generator handling of `{DynamicResource ...}` tokens in `Setter` values.

Purpose
- Ensure the generator emits a `DynamicResourceExtension` expression for setter literal tokens instead of passing the token through a TypeConverter (which could call `Brush.Parse`).

Build & Run
```powershell
dotnet build ./DynamicResourceSample.csproj -f net10.0-windows
dotnet run --project ./DynamicResourceSample.csproj -f net10.0-windows
```