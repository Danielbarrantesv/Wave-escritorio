// GlobalUsings.cs — Alias globales para desambiguar tipos WPF vs WinForms
// Necesario porque UseWindowsForms=true en el csproj hace que ambos namespaces
// (System.Windows.Controls y System.Windows.Forms) estén disponibles simultáneamente.

global using Application = System.Windows.Application;
global using Brush = System.Windows.Media.Brush;
global using UserControl = System.Windows.Controls.UserControl;
global using ContentControl = System.Windows.Controls.ContentControl;
global using DragEventArgs = System.Windows.DragEventArgs;
global using DragDropEffects = System.Windows.DragDropEffects;
global using DataFormats = System.Windows.DataFormats;
global using MessageBox = System.Windows.MessageBox;
global using SortOrder = Wave.Windows.Models.SortOrder;
