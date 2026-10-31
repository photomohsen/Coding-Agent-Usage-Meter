// PROTOTYPE: throwaway. One dark look (Windows 11 dark) for the widget, its menu, tooltips and the Settings window.
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;

namespace UsageWidget.Prototype;

static class Theme
{
    public static readonly Brush Normal = Hex("#4FC3F7"), Amber = Hex("#FFB300"), Red = Hex("#EF5350"),
        // Stale: grey fill and dimmed numbers. Critical: an action is needed (sign in), not a high Usage.
        StaleFill = Hex("#7A7A7A"), StaleText = Hex("#A0A0A0"), Critical = Hex("#FF99A4"),
        Track = Hex("#1FFFFFFF"), Primary = Hex("#F5F5F5"), Secondary = Hex("#C5C5C5"), Tertiary = Hex("#9D9D9D"),
        CardBg = Hex("#202020"), Edge = Hex("#26FFFFFF"), Divider = Hex("#24FFFFFF"),
        // The dot in front of a running dev server.
        Running = Hex("#66BB6A"),
        // Meter look: the unlit blocks keep a hint of their colour, so the amber and red end shows before it lights up.
        MeterOff = Hex("#1AFFFFFF"), MeterOffAmber = Hex("#29FFB300"), MeterOffRed = Hex("#2EEF5350");

    public static readonly FontFamily Font = new("Segoe UI Variable Text, Segoe UI"),
        Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public const string ResetGlyph = "", WarningGlyph = "";

    static SolidColorBrush Hex(string hex)
    {
        var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        b.Freeze();
        return b;
    }

    /// The menu, tooltip and Settings control styles, for Application.Resources.
    public static ResourceDictionary Load() => (ResourceDictionary)XamlReader.Parse(Xaml);

    const string Xaml = """
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <FontFamily x:Key="UiFont">Segoe UI Variable Text, Segoe UI</FontFamily>
  <FontFamily x:Key="IconFont">Segoe Fluent Icons, Segoe MDL2 Assets</FontFamily>
  <SolidColorBrush x:Key="Accent" Color="#4FC3F7"/>
  <SolidColorBrush x:Key="FlyoutBg" Color="#2C2C2C"/>
  <SolidColorBrush x:Key="FlyoutEdge" Color="#1FFFFFFF"/>
  <SolidColorBrush x:Key="Hover" Color="#14FFFFFF"/>
  <DropShadowEffect x:Key="Shadow" BlurRadius="18" ShadowDepth="4" Direction="270" Opacity="0.45" Color="Black"/>

  <Style TargetType="ContextMenu">
    <Setter Property="OverridesDefaultStyle" Value="True"/>
    <Setter Property="HasDropShadow" Value="False"/>
    <Setter Property="Foreground" Value="#F5F5F5"/>
    <Setter Property="FontFamily" Value="{StaticResource UiFont}"/>
    <Setter Property="FontSize" Value="13"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ContextMenu">
          <Border Margin="10,6,10,14" Background="{StaticResource FlyoutBg}" BorderBrush="{StaticResource FlyoutEdge}"
                  BorderThickness="1" CornerRadius="8" Padding="4" Effect="{StaticResource Shadow}" MinWidth="200">
            <ItemsPresenter KeyboardNavigation.DirectionalNavigation="Cycle"/>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType="MenuItem">
    <Setter Property="OverridesDefaultStyle" Value="True"/>
    <Setter Property="Foreground" Value="#F5F5F5"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="MenuItem">
          <Border x:Name="Bd" Background="Transparent" CornerRadius="4" Height="32" Padding="8,0,12,0">
            <Grid>
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width="20"/>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
              </Grid.ColumnDefinitions>
              <TextBlock x:Name="Check" Text="&#xE73E;" FontFamily="{StaticResource IconFont}" FontSize="12"
                         Foreground="{StaticResource Accent}" VerticalAlignment="Center" Visibility="Hidden"/>
              <ContentPresenter Grid.Column="1" ContentSource="Header" VerticalAlignment="Center" RecognizesAccessKey="True"/>
              <TextBlock Grid.Column="2" Text="{TemplateBinding InputGestureText}" Foreground="#9D9D9D" FontSize="12"
                         Margin="24,0,0,0" VerticalAlignment="Center"/>
            </Grid>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsHighlighted" Value="True">
              <Setter TargetName="Bd" Property="Background" Value="{StaticResource Hover}"/>
            </Trigger>
            <Trigger Property="IsChecked" Value="True">
              <Setter TargetName="Check" Property="Visibility" Value="Visible"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key="{x:Static MenuItem.SeparatorStyleKey}" TargetType="Separator">
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="Separator">
          <Border Height="1" Background="{StaticResource FlyoutEdge}" Margin="-4,4"/>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType="ToolTip">
    <Setter Property="OverridesDefaultStyle" Value="True"/>
    <Setter Property="HasDropShadow" Value="False"/>
    <Setter Property="Foreground" Value="#F5F5F5"/>
    <Setter Property="FontFamily" Value="{StaticResource UiFont}"/>
    <Setter Property="FontSize" Value="12"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ToolTip">
          <Border Margin="6,2,6,10" Background="{StaticResource FlyoutBg}" BorderBrush="{StaticResource FlyoutEdge}"
                  BorderThickness="1" CornerRadius="4" Padding="9,6" Effect="{StaticResource Shadow}" MaxWidth="360">
            <ContentPresenter>
              <ContentPresenter.Resources>
                <Style TargetType="TextBlock"><Setter Property="TextWrapping" Value="Wrap"/></Style>
              </ContentPresenter.Resources>
            </ContentPresenter>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Hover on a source: a card in the widget's style. MiniTip is Halo's one line. -->
  <Style x:Key="CardTip" TargetType="ToolTip">
    <Setter Property="OverridesDefaultStyle" Value="True"/>
    <Setter Property="HasDropShadow" Value="False"/>
    <Setter Property="Foreground" Value="#F5F5F5"/>
    <Setter Property="FontFamily" Value="{StaticResource UiFont}"/>
    <Setter Property="FontSize" Value="12"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ToolTip">
          <Border Margin="6,2,6,10" Background="#2B2B2B" BorderBrush="#29FFFFFF" BorderThickness="1" CornerRadius="8"
                  Padding="10,8" Effect="{StaticResource Shadow}" MaxWidth="360">
            <ContentPresenter/>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key="MiniTip" TargetType="ToolTip" BasedOn="{StaticResource CardTip}">
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ToolTip">
          <Border Margin="6,2,6,10" Background="#2B2B2B" BorderBrush="#29FFFFFF" BorderThickness="1" CornerRadius="6"
                  Padding="8,4" Effect="{StaticResource Shadow}">
            <ContentPresenter/>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Dev servers: Close at the end of a row, and Halo's short × -->
  <Style x:Key="ServerClose" TargetType="Button">
    <Setter Property="Foreground" Value="#EF5350"/>
    <Setter Property="FontSize" Value="10"/>
    <Setter Property="Cursor" Value="Hand"/>
    <Setter Property="VerticalAlignment" Value="Center"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="Button">
          <Border x:Name="Bd" Background="Transparent" BorderBrush="#EF5350" BorderThickness="1" CornerRadius="4" Padding="6,0,6,1">
            <ContentPresenter VerticalAlignment="Center"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Bd" Property="Background" Value="#29EF5350"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key="ServerX" TargetType="Button">
    <Setter Property="Foreground" Value="#9D9D9D"/>
    <Setter Property="Cursor" Value="Hand"/>
    <Setter Property="VerticalAlignment" Value="Center"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="Button">
          <Border x:Name="Bd" Background="Transparent" CornerRadius="3" Width="14" Height="14">
            <Path Data="M1,1 L7,7 M7,1 L1,7" Stroke="{TemplateBinding Foreground}" StrokeThickness="1.4"
                  StrokeStartLineCap="Round" StrokeEndLineCap="Round" HorizontalAlignment="Center" VerticalAlignment="Center"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Bd" Property="Background" Value="#29EF5350"/>
              <Setter Property="Foreground" Value="#EF5350"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- The Close confirmation's buttons: grey, or red for the one that stops it. -->
  <Style x:Key="DialogButton" TargetType="Button">
    <Setter Property="Foreground" Value="#F5F5F5"/>
    <Setter Property="Background" Value="#14FFFFFF"/>
    <Setter Property="FontSize" Value="12"/>
    <Setter Property="Cursor" Value="Hand"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="Button">
          <Grid>
            <Border Background="{TemplateBinding Background}" CornerRadius="4"/>
            <Border x:Name="Hi" Background="#14FFFFFF" CornerRadius="4" Visibility="Hidden"/>
            <ContentPresenter Margin="12,3,12,4" HorizontalAlignment="Center" VerticalAlignment="Center"/>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Hi" Property="Visibility" Value="Visible"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Settings: segmented choice (Look, Update every). -->
  <Style x:Key="Segment" TargetType="RadioButton">
    <Setter Property="Foreground" Value="#C5C5C5"/>
    <Setter Property="FontSize" Value="12"/>
    <Setter Property="Cursor" Value="Hand"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="RadioButton">
          <Border x:Name="Bd" Background="Transparent" CornerRadius="4" Height="26" Padding="2,0">
            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Bd" Property="Background" Value="{StaticResource Hover}"/>
            </Trigger>
            <Trigger Property="IsChecked" Value="True">
              <Setter TargetName="Bd" Property="Background" Value="{StaticResource Accent}"/>
              <Setter Property="Foreground" Value="#0B1A22"/>
              <Setter Property="FontWeight" Value="SemiBold"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Settings: on/off switch, label on the left. -->
  <Style x:Key="Switch" TargetType="CheckBox">
    <Setter Property="Foreground" Value="#F5F5F5"/>
    <Setter Property="FontSize" Value="13"/>
    <Setter Property="Cursor" Value="Hand"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="CheckBox">
          <Grid Background="Transparent" Height="40">
            <Grid.ColumnDefinitions>
              <ColumnDefinition Width="*"/>
              <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>
            <ContentPresenter VerticalAlignment="Center"/>
            <Border x:Name="Track" Grid.Column="1" Width="40" Height="20" CornerRadius="10" BorderThickness="1"
                    BorderBrush="#9D9D9D" Background="Transparent" VerticalAlignment="Center">
              <Ellipse x:Name="Knob" Width="12" Height="12" Fill="#C5C5C5" HorizontalAlignment="Left" Margin="4,0"/>
            </Border>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsChecked" Value="True">
              <Setter TargetName="Track" Property="Background" Value="{StaticResource Accent}"/>
              <Setter TargetName="Track" Property="BorderBrush" Value="{StaticResource Accent}"/>
              <Setter TargetName="Knob" Property="Fill" Value="#1A1A1A"/>
              <Setter TargetName="Knob" Property="HorizontalAlignment" Value="Right"/>
            </Trigger>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Knob" Property="Width" Value="14"/>
              <Setter TargetName="Knob" Property="Height" Value="14"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key="SliderFill" TargetType="RepeatButton">
    <Setter Property="Focusable" Value="False"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="RepeatButton">
          <Grid Background="Transparent"><Border Height="4" CornerRadius="2" Background="{StaticResource Accent}"/></Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key="SliderRest" TargetType="RepeatButton">
    <Setter Property="Focusable" Value="False"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="RepeatButton">
          <Grid Background="Transparent"><Border Height="4" CornerRadius="2" Background="#33FFFFFF"/></Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key="Fluent" TargetType="Slider">
    <Setter Property="Cursor" Value="Hand"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="Slider">
          <Track x:Name="PART_Track" Height="22">
            <Track.DecreaseRepeatButton><RepeatButton Style="{StaticResource SliderFill}" Command="Slider.DecreaseLarge"/></Track.DecreaseRepeatButton>
            <Track.IncreaseRepeatButton><RepeatButton Style="{StaticResource SliderRest}" Command="Slider.IncreaseLarge"/></Track.IncreaseRepeatButton>
            <Track.Thumb>
              <Thumb>
                <Thumb.Template>
                  <ControlTemplate TargetType="Thumb">
                    <Grid Width="20" Height="20">
                      <Ellipse Fill="#2C2C2C" Stroke="#1FFFFFFF" StrokeThickness="1"/>
                      <Ellipse x:Name="Dot" Width="12" Height="12" Fill="{StaticResource Accent}"/>
                    </Grid>
                    <ControlTemplate.Triggers>
                      <Trigger Property="IsMouseOver" Value="True">
                        <Setter TargetName="Dot" Property="Width" Value="14"/>
                        <Setter TargetName="Dot" Property="Height" Value="14"/>
                      </Trigger>
                    </ControlTemplate.Triggers>
                  </ControlTemplate>
                </Thumb.Template>
              </Thumb>
            </Track.Thumb>
          </Track>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Settings: text buttons and the window's close button. -->
  <Style x:Key="Link" TargetType="Button">
    <Setter Property="Foreground" Value="{StaticResource Accent}"/>
    <Setter Property="FontSize" Value="12"/>
    <Setter Property="Cursor" Value="Hand"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="Button">
          <Border x:Name="Bd" Background="Transparent" CornerRadius="4" Padding="8,5">
            <ContentPresenter VerticalAlignment="Center"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Bd" Property="Background" Value="{StaticResource Hover}"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key="WindowClose" TargetType="Button">
    <Setter Property="Foreground" Value="#C5C5C5"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="Button">
          <Border x:Name="Bd" Background="Transparent" CornerRadius="4" Width="32" Height="28">
            <TextBlock Text="&#xE8BB;" FontFamily="{StaticResource IconFont}" FontSize="10"
                       Foreground="{TemplateBinding Foreground}" HorizontalAlignment="Center" VerticalAlignment="Center"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Bd" Property="Background" Value="#C42B1C"/>
              <Setter Property="Foreground" Value="#FFFFFF"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
</ResourceDictionary>
""";
}
