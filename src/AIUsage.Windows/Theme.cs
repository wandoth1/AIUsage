using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;

namespace AIUsage.Windows;

internal static class Theme
{
    public static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
    public static void Apply(bool light)
    {
        var r = Application.Current.Resources;
        string[] names = ["Page", "Surface", "Raised", "Ink", "Muted", "Line", "Accent", "Tint", "Warning"];
        string[] dark = ["#11151F", "#191F2D", "#222B3E", "#F0F3FA", "#A5AFC5", "#30394C", "#79A7FF", "#233657", "#F0BD6A"];
        string[] pale = ["#F3F5F9", "#FFFFFF", "#EAF0F8", "#182337", "#55657D", "#D9E1EC", "#245DC0", "#E5EEFF", "#8C5700"];
        for (int i = 0; i < names.Length; i++) r[names[i]] = (SolidColorBrush)new BrushConverter().ConvertFromString((light ? pale : dark)[i])!;
        const string styles = """
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <Style TargetType="TextBlock"><Setter Property="Foreground" Value="{DynamicResource Ink}"/><Setter Property="FontFamily" Value="Segoe UI"/></Style>
  <Style TargetType="Button">
    <Setter Property="Background" Value="{DynamicResource Raised}"/><Setter Property="Foreground" Value="{DynamicResource Ink}"/>
    <Setter Property="Padding" Value="13,8"/><Setter Property="Margin" Value="0,0,6,0"/><Setter Property="BorderThickness" Value="0"/>
    <Setter Property="Cursor" Value="Hand"/><Setter Property="FontSize" Value="12"/>
    <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button">
      <Border x:Name="Box" Background="{TemplateBinding Background}" CornerRadius="7" Padding="{TemplateBinding Padding}">
        <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
      </Border>
      <ControlTemplate.Triggers>
        <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Box" Property="Opacity" Value="0.78"/></Trigger>
        <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Box" Property="BorderBrush" Value="{DynamicResource Accent}"/><Setter TargetName="Box" Property="BorderThickness" Value="1"/></Trigger>
        <Trigger Property="IsEnabled" Value="False"><Setter TargetName="Box" Property="Opacity" Value="0.45"/></Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType="TextBox"><Setter Property="Foreground" Value="{DynamicResource Ink}"/><Setter Property="Background" Value="{DynamicResource Raised}"/><Setter Property="BorderBrush" Value="{DynamicResource Line}"/><Setter Property="Padding" Value="10"/><Setter Property="FontSize" Value="13"/><Setter Property="CaretBrush" Value="{DynamicResource Ink}"/></Style>
  <Style TargetType="CheckBox"><Setter Property="Foreground" Value="{DynamicResource Ink}"/><Setter Property="Margin" Value="0,10,0,8"/><Setter Property="FontSize" Value="13"/></Style>
</ResourceDictionary>
""";
        r.MergedDictionaries.Clear(); r.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(styles));
    }
}
