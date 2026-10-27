namespace VividDesk.Core;

/// <summary>
/// Масштаб интерфейса (0.4..1.0). Базовая вёрстка окон проектируется в «крупных» размерах,
/// затем умножается на этот коэффициент: сам контент (LayoutTransform), размеры окон,
/// caption-зоны и рамки ресайза. Изменение применяется ко всем открытым окнам на лету.
/// </summary>
public static class UiScale
{
    public static event Action<double> Changed;

    /// <summary>Текущий масштаб. До явной инициализации — 0.75.</summary>
    public static double Current { get; private set; } = 0.75;

    /// <summary>Установить масштаб при старте (без уведомления окон).</summary>
    public static void Init(double value) => Current = Clamp(value);

    /// <summary>Сменить масштаб и уведомить открытые окна.</summary>
    public static void Set(double value)
    {
        Current = Clamp(value);
        Changed?.Invoke(Current);
    }

    public static double Clamp(double v) => Math.Min(1.0, Math.Max(0.4, Math.Round(v, 2)));
}
