using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace XboxMetroLauncher.Controls;

public partial class MetroTile : UserControl
{
	public static readonly DependencyProperty TitleProperty = DependencyProperty.Register("Title", typeof(string), typeof(MetroTile), new PropertyMetadata((object)string.Empty));

	public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register("Subtitle", typeof(string), typeof(MetroTile), new PropertyMetadata((object)string.Empty));

	public static readonly DependencyProperty ImagePathProperty = DependencyProperty.Register("ImagePath", typeof(string), typeof(MetroTile), new PropertyMetadata((object)string.Empty));

	public static readonly DependencyProperty TileBrushProperty = DependencyProperty.Register("TileBrush", typeof(Brush), typeof(MetroTile), new PropertyMetadata((object)Brushes.SeaGreen));

	public static readonly DependencyProperty TileWidthProperty = DependencyProperty.Register("TileWidth", typeof(double), typeof(MetroTile), new PropertyMetadata((object)250.0));

	public static readonly DependencyProperty TileHeightProperty = DependencyProperty.Register("TileHeight", typeof(double), typeof(MetroTile), new PropertyMetadata((object)140.0));

	public static readonly DependencyProperty CommandProperty = DependencyProperty.Register("Command", typeof(ICommand), typeof(MetroTile), new PropertyMetadata((PropertyChangedCallback)null));

	public static readonly DependencyProperty CommandParameterProperty = DependencyProperty.Register("CommandParameter", typeof(object), typeof(MetroTile), new PropertyMetadata((PropertyChangedCallback)null));

	public static readonly DependencyProperty FocusCommandProperty = DependencyProperty.Register("FocusCommand", typeof(ICommand), typeof(MetroTile), new PropertyMetadata((PropertyChangedCallback)null));

	public string Title
	{
		get
		{
			return (string)((DependencyObject)this).GetValue(TitleProperty);
		}
		set
		{
			((DependencyObject)this).SetValue(TitleProperty, (object)value);
		}
	}

	public string Subtitle
	{
		get
		{
			return (string)((DependencyObject)this).GetValue(SubtitleProperty);
		}
		set
		{
			((DependencyObject)this).SetValue(SubtitleProperty, (object)value);
		}
	}

	public string ImagePath
	{
		get
		{
			return (string)((DependencyObject)this).GetValue(ImagePathProperty);
		}
		set
		{
			((DependencyObject)this).SetValue(ImagePathProperty, (object)value);
		}
	}

	public Brush TileBrush
	{
		get
		{
			return (Brush)((DependencyObject)this).GetValue(TileBrushProperty);
		}
		set
		{
			((DependencyObject)this).SetValue(TileBrushProperty, (object)value);
		}
	}

	public double TileWidth
	{
		get
		{
			return (double)((DependencyObject)this).GetValue(TileWidthProperty);
		}
		set
		{
			((DependencyObject)this).SetValue(TileWidthProperty, (object)value);
		}
	}

	public double TileHeight
	{
		get
		{
			return (double)((DependencyObject)this).GetValue(TileHeightProperty);
		}
		set
		{
			((DependencyObject)this).SetValue(TileHeightProperty, (object)value);
		}
	}

	public ICommand? Command
	{
		get
		{
			return (ICommand)((DependencyObject)this).GetValue(CommandProperty);
		}
		set
		{
			((DependencyObject)this).SetValue(CommandProperty, (object)value);
		}
	}

	public object? CommandParameter
	{
		get
		{
			return ((DependencyObject)this).GetValue(CommandParameterProperty);
		}
		set
		{
			((DependencyObject)this).SetValue(CommandParameterProperty, value);
		}
	}

	public ICommand? FocusCommand
	{
		get
		{
			return (ICommand)((DependencyObject)this).GetValue(FocusCommandProperty);
		}
		set
		{
			((DependencyObject)this).SetValue(FocusCommandProperty, (object)value);
		}
	}

	public MetroTile()
	{
		InitializeComponent();
	}

	private void TileButton_OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
	{
		ICommand? focusCommand = FocusCommand;
		if (focusCommand != null && focusCommand.CanExecute(CommandParameter))
		{
			FocusCommand.Execute(CommandParameter);
		}
	}
}
