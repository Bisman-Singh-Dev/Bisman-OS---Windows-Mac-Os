// Bisman OS 2 - control templates (scrollbars, switches, sliders, menus, tooltips) in one shared dictionary.
namespace BismanOS;

public static class Styles
{
    public static void Install(Application app)
    {
        var rd = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(Xaml);
        app.Resources.MergedDictionaries.Add(rd);
        app.Resources["AccentBrush"] = Th.Accent.Clone();
        app.Resources["AccentSoftBrush"] = Th.AccentSoft.Clone();
    }

    public static Style Get(string key) => (Style)Application.Current.Resources[key];

    const string Xaml = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>

  <Style x:Key='BarThumb' TargetType='Thumb'>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Thumb'>
      <Border x:Name='b' CornerRadius='3' Background='#40FFFFFF'/>
      <ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Background' Value='#70FFFFFF'/></Trigger></ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>

  <Style TargetType='ScrollBar'>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='Width' Value='10'/><Setter Property='MinWidth' Value='10'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'>
      <Grid Background='Transparent'>
        <Track x:Name='PART_Track' IsDirectionReversed='True'>
          <Track.Thumb><Thumb Style='{StaticResource BarThumb}' Margin='3,2'/></Track.Thumb>
        </Track>
      </Grid>
    </ControlTemplate></Setter.Value></Setter>
    <Style.Triggers>
      <Trigger Property='Orientation' Value='Horizontal'>
        <Setter Property='Width' Value='Auto'/><Setter Property='MinWidth' Value='0'/>
        <Setter Property='Height' Value='10'/><Setter Property='MinHeight' Value='10'/>
        <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'>
          <Grid Background='Transparent'>
            <Track x:Name='PART_Track' IsDirectionReversed='False'>
              <Track.Thumb><Thumb Style='{StaticResource BarThumb}' Margin='2,3'/></Track.Thumb>
            </Track>
          </Grid>
        </ControlTemplate></Setter.Value></Setter>
      </Trigger>
    </Style.Triggers>
  </Style>

  <Style x:Key='Switch' TargetType='ToggleButton'>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ToggleButton'>
      <Grid Width='42' Height='24' Background='Transparent'>
        <Border x:Name='track' CornerRadius='12' Background='#33FFFFFF' BorderBrush='#22FFFFFF' BorderThickness='1'/>
        <Ellipse x:Name='knob' Width='20' Height='20' Fill='White' HorizontalAlignment='Left' Margin='2,0,0,0'>
          <Ellipse.RenderTransform><TranslateTransform X='0'/></Ellipse.RenderTransform>
          <Ellipse.Effect><DropShadowEffect BlurRadius='4' ShadowDepth='1' Opacity='0.35'/></Ellipse.Effect>
        </Ellipse>
      </Grid>
      <ControlTemplate.Triggers>
        <Trigger Property='IsChecked' Value='True'>
          <Setter TargetName='track' Property='Background' Value='{DynamicResource AccentBrush}'/>
          <Trigger.EnterActions><BeginStoryboard><Storyboard>
            <DoubleAnimation Storyboard.TargetName='knob' Storyboard.TargetProperty='(UIElement.RenderTransform).(TranslateTransform.X)' To='18' Duration='0:0:0.2'>
              <DoubleAnimation.EasingFunction><BackEase Amplitude='0.3' EasingMode='EaseOut'/></DoubleAnimation.EasingFunction>
            </DoubleAnimation>
          </Storyboard></BeginStoryboard></Trigger.EnterActions>
          <Trigger.ExitActions><BeginStoryboard><Storyboard>
            <DoubleAnimation Storyboard.TargetName='knob' Storyboard.TargetProperty='(UIElement.RenderTransform).(TranslateTransform.X)' To='0' Duration='0:0:0.2'>
              <DoubleAnimation.EasingFunction><BackEase Amplitude='0.3' EasingMode='EaseOut'/></DoubleAnimation.EasingFunction>
            </DoubleAnimation>
          </Storyboard></BeginStoryboard></Trigger.ExitActions>
        </Trigger>
        <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>

  <Style x:Key='SliderFill' TargetType='RepeatButton'>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='RepeatButton'>
      <Border Height='6' CornerRadius='3' Background='{DynamicResource AccentBrush}' VerticalAlignment='Center'/>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style x:Key='SliderEmpty' TargetType='RepeatButton'>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='RepeatButton'>
      <Border Background='Transparent'/>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style x:Key='SliderKnob' TargetType='Thumb'>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Thumb'>
      <Ellipse Width='18' Height='18' Fill='White'>
        <Ellipse.Effect><DropShadowEffect BlurRadius='5' ShadowDepth='1' Opacity='0.4'/></Ellipse.Effect>
      </Ellipse>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style x:Key='Glider' TargetType='Slider'>
    <Setter Property='IsMoveToPointEnabled' Value='True'/>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Slider'>
      <Grid Height='24' Background='Transparent'>
        <Border Height='6' CornerRadius='3' Background='#2EFFFFFF' VerticalAlignment='Center'/>
        <Track x:Name='PART_Track'>
          <Track.DecreaseRepeatButton><RepeatButton Command='Slider.DecreaseLarge' Style='{StaticResource SliderFill}'/></Track.DecreaseRepeatButton>
          <Track.IncreaseRepeatButton><RepeatButton Command='Slider.IncreaseLarge' Style='{StaticResource SliderEmpty}'/></Track.IncreaseRepeatButton>
          <Track.Thumb><Thumb Style='{StaticResource SliderKnob}'/></Track.Thumb>
        </Track>
      </Grid>
    </ControlTemplate></Setter.Value></Setter>
  </Style>

  <Style x:Key='Grip' TargetType='Thumb'>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Thumb'><Border Background='Transparent'/></ControlTemplate></Setter.Value></Setter>
  </Style>

  <Style TargetType='ContextMenu'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='HasDropShadow' Value='True'/>
    <Setter Property='Foreground' Value='#F3F6FB'/>
    <Setter Property='FontFamily' Value='Segoe UI Variable Text, Segoe UI'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ContextMenu'>
      <Border Background='#F2181C26' CornerRadius='11' BorderBrush='#30FFFFFF' BorderThickness='1' Padding='5' Margin='0,0,14,14'>
        <Border.Effect><DropShadowEffect BlurRadius='22' ShadowDepth='5' Opacity='0.5' Direction='270'/></Border.Effect>
        <StackPanel IsItemsHost='True' KeyboardNavigation.DirectionalNavigation='Cycle'/>
      </Border>
    </ControlTemplate></Setter.Value></Setter>
  </Style>

  <Style TargetType='MenuItem'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Foreground' Value='#F3F6FB'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='MenuItem'>
      <Border x:Name='bd' CornerRadius='7' Padding='8,6,16,6' Background='Transparent' MinWidth='200'>
        <Grid>
          <Grid.ColumnDefinitions><ColumnDefinition Width='26'/><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions>
          <ContentPresenter ContentSource='Icon' VerticalAlignment='Center' HorizontalAlignment='Left'/>
          <ContentPresenter Grid.Column='1' ContentSource='Header' RecognizesAccessKey='True' VerticalAlignment='Center'/>
          <TextBlock Grid.Column='2' Text='{TemplateBinding InputGestureText}' Opacity='0.5' Margin='18,0,0,0' VerticalAlignment='Center'/>
        </Grid>
      </Border>
      <ControlTemplate.Triggers>
        <Trigger Property='IsHighlighted' Value='True'><Setter TargetName='bd' Property='Background' Value='{DynamicResource AccentBrush}'/></Trigger>
        <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>

  <Style x:Key='{x:Static MenuItem.SeparatorStyleKey}' TargetType='Separator'>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Separator'><Border Height='1' Margin='8,4' Background='#22FFFFFF'/></ControlTemplate></Setter.Value></Setter>
  </Style>

  <Style TargetType='ToolTip'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Foreground' Value='#F3F6FB'/>
    <Setter Property='FontSize' Value='12'/>
    <Setter Property='FontFamily' Value='Segoe UI Variable Text, Segoe UI'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ToolTip'>
      <Border Background='#F2181C26' CornerRadius='7' BorderBrush='#30FFFFFF' BorderThickness='1' Padding='9,5'>
        <ContentPresenter/>
      </Border>
    </ControlTemplate></Setter.Value></Setter>
  </Style>

  <Style x:Key='Field' TargetType='TextBox'>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='BorderThickness' Value='0'/>
    <Setter Property='Foreground' Value='#F3F6FB'/>
    <Setter Property='CaretBrush' Value='{DynamicResource AccentBrush}'/>
    <Setter Property='SelectionBrush' Value='{DynamicResource AccentBrush}'/>
    <Setter Property='VerticalContentAlignment' Value='Center'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='TextBox'>
      <ScrollViewer x:Name='PART_ContentHost' Focusable='False' HorizontalScrollBarVisibility='Hidden' VerticalScrollBarVisibility='Hidden' VerticalAlignment='Center'/>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
</ResourceDictionary>";
}
