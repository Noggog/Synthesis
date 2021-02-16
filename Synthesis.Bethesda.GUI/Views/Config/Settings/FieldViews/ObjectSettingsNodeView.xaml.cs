using Noggog;
using Noggog.WPF;
using ReactiveUI;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows;

namespace Synthesis.Bethesda.GUI.Views
{
    public class ObjectSettingsNodeViewBase : NoggogUserControl<ObjectSettingsVM> { }

    /// <summary>
    /// Interaction logic for ObjectSettingsNodeView.xaml
    /// </summary>
    public partial class ObjectSettingsNodeView : ObjectSettingsNodeViewBase
    {
        public ObjectSettingsNodeView()
        {
            InitializeComponent();
            this.WhenActivated(disposable =>
            {
                this.WhenAnyValue(x => x.ViewModel!.MemberName)
                    .BindToStrict(this, x => x.SettingNameBlock.Text)
                    .DisposeWith(disposable);
                this.WhenAnyValue(x => x.ViewModel!.MemberName)
                    .Select(x => x.IsNullOrWhitespace() ? Visibility.Collapsed : Visibility.Visible)
                    .BindToStrict(this, x => x.SettingNameBlock.Visibility)
                    .DisposeWith(disposable);
            });
        }
    }
}
