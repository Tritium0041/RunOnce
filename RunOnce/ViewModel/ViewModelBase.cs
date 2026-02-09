/*
 * ViewModel 基类
 * 提供 INotifyPropertyChanged 的通用实现，作为所有 ViewModel 的基类
 *
 * @author: WaterRun
 * @file: ViewModel/ViewModelBase.cs
 * @date: 2026-02-09
 */

#nullable enable

using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RunOnce.ViewModel;

/// <summary>
/// ViewModel 基类，提供属性变更通知的通用实现。
/// </summary>
/// <remarks>
/// 不变量：PropertyChanged 事件仅在属性值实际发生变化时触发。
/// 线程安全：非线程安全，所有属性变更应在 UI 线程执行。
/// 副作用：无。
/// </remarks>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    /// <summary>
    /// 属性值变更时触发的事件。
    /// </summary>
    /// <remarks>
    /// 触发时机：调用 <see cref="SetProperty{T}"/> 且新旧值不相等时，或显式调用 <see cref="OnPropertyChanged"/>。
    /// 线程上下文：在调用线程触发，通常为 UI 线程。
    /// </remarks>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// 触发指定属性的变更通知。
    /// </summary>
    /// <param name="propertyName">变更的属性名称，由编译器自动填充。不允许为 null。</param>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// 设置属性后备字段的值，若值发生变化则触发变更通知。
    /// </summary>
    /// <typeparam name="T">属性值的类型。</typeparam>
    /// <param name="field">属性的后备字段引用。</param>
    /// <param name="value">待设置的新值。</param>
    /// <param name="propertyName">属性名称，由编译器自动填充。不允许为 null。</param>
    /// <returns>若值发生变化并已通知则返回 true，否则返回 false。</returns>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}