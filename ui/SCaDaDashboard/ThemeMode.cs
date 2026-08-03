namespace SCaDaDashboard;

// .NET 9 WPF, Application/Window'a yerlesik bir "ThemeMode" ozelligi ekledi
// (deneysel, WPF0001). Alt siniflarin icinde ciplak "ThemeMode" o mirasa
// cozumleniyordu -> CS0176. Cakismayi kesmek icin enum "AppThemeMode".
public enum AppThemeMode { Dark, Light }
