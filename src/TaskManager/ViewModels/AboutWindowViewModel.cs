using CommunityToolkit.Mvvm.ComponentModel;

namespace TaskManager.ViewModels
{
    /// <summary>
    /// Viewmodel for <see cref="TaskManager.UI.Views.AboutWindow"/>. The About content is static
    /// assembly metadata rendered directly by the view; this type exists so every window keeps a
    /// paired viewmodel (enforced by ViewViewmodelTests) and the dialog factory stays uniform.
    /// </summary>
    internal class AboutWindowViewModel : ObservableObject
    {
    }
}
