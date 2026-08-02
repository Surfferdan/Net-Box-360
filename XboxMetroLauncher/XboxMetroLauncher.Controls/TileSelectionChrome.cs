using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace XboxMetroLauncher.Controls;

public static class TileSelectionChrome
{
	public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(TileSelectionChrome), new PropertyMetadata((object)false, new PropertyChangedCallback(OnIsEnabledChanged)));

	public static readonly DependencyProperty IsActiveProperty = DependencyProperty.RegisterAttached("IsActive", typeof(bool), typeof(TileSelectionChrome), new PropertyMetadata((object)false));

	private static Button? _activeButton;

	public static bool GetIsEnabled(DependencyObject element)
	{
		return (bool)element.GetValue(IsEnabledProperty);
	}

	public static void SetIsEnabled(DependencyObject element, bool value)
	{
		element.SetValue(IsEnabledProperty, (object)value);
	}

	public static bool GetIsActive(DependencyObject element)
	{
		return (bool)element.GetValue(IsActiveProperty);
	}

	public static void SetIsActive(DependencyObject element, bool value)
	{
		element.SetValue(IsActiveProperty, (object)value);
	}

	private static void OnIsEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
	{
		if (element is Button button)
		{
			if ((bool)e.NewValue)
			{
				button.MouseEnter += Button_OnMouseEnter;
				button.MouseLeave += Button_OnMouseLeave;
				button.GotKeyboardFocus += Button_OnGotKeyboardFocus;
				button.LostKeyboardFocus += Button_OnLostKeyboardFocus;
				button.Unloaded += Button_OnUnloaded;
			}
			else
			{
				button.MouseEnter -= Button_OnMouseEnter;
				button.MouseLeave -= Button_OnMouseLeave;
				button.GotKeyboardFocus -= Button_OnGotKeyboardFocus;
				button.LostKeyboardFocus -= Button_OnLostKeyboardFocus;
				button.Unloaded -= Button_OnUnloaded;
				ClearIfActive(button);
			}
		}
	}

	private static void Button_OnMouseEnter(object sender, MouseEventArgs e)
	{
		if (sender is Button { IsEnabled: not false } button)
		{
			SetActive(button);
		}
	}

	private static void Button_OnMouseLeave(object sender, MouseEventArgs e)
	{
		if (sender is Button button && _activeButton == button)
		{
			ClearActive();
		}
	}

	private static void Button_OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
	{
		if (sender is Button { IsEnabled: not false } button)
		{
			SetActive(button);
		}
	}

	private static void Button_OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
	{
		if (sender is Button button && _activeButton == button && !button.IsMouseOver)
		{
			ClearActive();
		}
	}

	private static void Button_OnUnloaded(object sender, RoutedEventArgs e)
	{
		if (sender is Button button)
		{
			ClearIfActive(button);
		}
	}

	private static void SetActive(Button button)
	{
		if (_activeButton != button)
		{
			ClearActive();
			_activeButton = button;
			SetIsActive((DependencyObject)(object)button, value: true);
		}
	}

	private static void ClearIfActive(Button button)
	{
		if (_activeButton == button)
		{
			ClearActive();
		}
		else
		{
			SetIsActive((DependencyObject)(object)button, value: false);
		}
	}

	private static void ClearActive()
	{
		if (_activeButton != null)
		{
			SetIsActive((DependencyObject)(object)_activeButton, value: false);
			_activeButton = null;
		}
	}
}
