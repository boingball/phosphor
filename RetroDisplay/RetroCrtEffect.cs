using System;
using System.Windows;
using System.Windows.Media.Effects;

namespace RetroDisplay
{
    public class RetroCrtEffect : ShaderEffect
    {
        private static readonly PixelShader shader = new PixelShader
        {
            UriSource = new Uri("/RetroDisplay;component/Shaders/RetroCrt.ps", UriKind.Relative)
        };

        public RetroCrtEffect()
        {
            PixelShader = shader;

            UpdateShaderValue(InputProperty);
            UpdateShaderValue(BrightnessProperty);
            UpdateShaderValue(ContrastProperty);
            UpdateShaderValue(SaturationProperty);
            UpdateShaderValue(ScanlineStrengthProperty);
            UpdateShaderValue(LineCountProperty);
            UpdateShaderValue(GammaProperty);
            UpdateShaderValue(PhosphorStrengthProperty);
        }

        public static readonly DependencyProperty InputProperty =
            RegisterPixelShaderSamplerProperty("Input", typeof(RetroCrtEffect), 0);

        public static readonly DependencyProperty BrightnessProperty =
            DependencyProperty.Register(nameof(Brightness), typeof(double), typeof(RetroCrtEffect),
                new UIPropertyMetadata(0.0, PixelShaderConstantCallback(0)));

        public static readonly DependencyProperty ContrastProperty =
            DependencyProperty.Register(nameof(Contrast), typeof(double), typeof(RetroCrtEffect),
                new UIPropertyMetadata(1.0, PixelShaderConstantCallback(1)));

        public static readonly DependencyProperty SaturationProperty =
            DependencyProperty.Register(nameof(Saturation), typeof(double), typeof(RetroCrtEffect),
                new UIPropertyMetadata(1.0, PixelShaderConstantCallback(2)));

        public static readonly DependencyProperty ScanlineStrengthProperty =
            DependencyProperty.Register(nameof(ScanlineStrength), typeof(double), typeof(RetroCrtEffect),
                new UIPropertyMetadata(0.0, PixelShaderConstantCallback(3)));

        public static readonly DependencyProperty LineCountProperty =
            DependencyProperty.Register(nameof(LineCount), typeof(double), typeof(RetroCrtEffect),
                new UIPropertyMetadata(576.0, PixelShaderConstantCallback(4)));

        public static readonly DependencyProperty GammaProperty =
            DependencyProperty.Register(nameof(Gamma), typeof(double), typeof(RetroCrtEffect),
        new UIPropertyMetadata(1.0, PixelShaderConstantCallback(5)));

        public static readonly DependencyProperty PhosphorStrengthProperty =
            DependencyProperty.Register(nameof(PhosphorStrength), typeof(double), typeof(RetroCrtEffect),
                new UIPropertyMetadata(0.0, PixelShaderConstantCallback(6)));

        public double ScreenWidth
        {
            get => (double)GetValue(ScreenWidthProperty);
            set => SetValue(ScreenWidthProperty, value);
        }
        public static readonly DependencyProperty ScreenWidthProperty =
            DependencyProperty.Register(nameof(ScreenWidth), typeof(double), typeof(RetroCrtEffect),
                new UIPropertyMetadata(0.0, PixelShaderConstantCallback(7)));

        public double ScreenHeight
        {
            get => (double)GetValue(ScreenHeightProperty);
            set => SetValue(ScreenHeightProperty, value);
        }
        public static readonly DependencyProperty ScreenHeightProperty =
            DependencyProperty.Register(nameof(ScreenHeight), typeof(double), typeof(RetroCrtEffect),
                new UIPropertyMetadata(0.0, PixelShaderConstantCallback(8)));

        public double EffectiveWidth
        {
            get => (double)GetValue(EffectiveWidthProperty);
            set => SetValue(EffectiveWidthProperty, value);
        }

        public static readonly DependencyProperty EffectiveWidthProperty =
            DependencyProperty.Register(
                nameof(EffectiveWidth),
                typeof(double),
                typeof(RetroCrtEffect),
                new UIPropertyMetadata(0.0, PixelShaderConstantCallback(9))
            );

        public double EffectiveHeight
        {
            get => (double)GetValue(EffectiveHeightProperty);
            set => SetValue(EffectiveHeightProperty, value);
        }

        public static readonly DependencyProperty EffectiveHeightProperty =
            DependencyProperty.Register(
                nameof(EffectiveHeight),
                typeof(double),
                typeof(RetroCrtEffect),
                new UIPropertyMetadata(0.0, PixelShaderConstantCallback(10))
            );

        public double ScanlinePhase
        {
            get => (double)GetValue(ScanlinePhaseProperty);
            set => SetValue(ScanlinePhaseProperty, value);
        }

        public static readonly DependencyProperty ScanlinePhaseProperty =
            DependencyProperty.Register(
                nameof(ScanlinePhase),
                typeof(double),
                typeof(RetroCrtEffect),
                new UIPropertyMetadata(0.0, PixelShaderConstantCallback(11))
            );

        public double MaskType
        {
            get => (double)GetValue(MaskTypeProperty);
            set => SetValue(MaskTypeProperty, value);
        }

        public static readonly DependencyProperty MaskTypeProperty =
            DependencyProperty.Register(
                nameof(MaskType),
                typeof(double),
                typeof(RetroCrtEffect),
                new UIPropertyMetadata(0.0, PixelShaderConstantCallback(12))
            );

        public double BeamWidth
        {
            get => (double)GetValue(BeamWidthProperty);
            set => SetValue(BeamWidthProperty, value);
        }

        public static readonly DependencyProperty BeamWidthProperty =
            DependencyProperty.Register(
                nameof(BeamWidth),
                typeof(double),
                typeof(RetroCrtEffect),
                new UIPropertyMetadata(0.18, PixelShaderConstantCallback(13))
            );


        public double Gamma
        {
            get => (double)GetValue(GammaProperty);
            set => SetValue(GammaProperty, value);
        }

        public double PhosphorStrength
        {
            get => (double)GetValue(PhosphorStrengthProperty);
            set => SetValue(PhosphorStrengthProperty, value);
        }

        public double Brightness { get => (double)GetValue(BrightnessProperty); set => SetValue(BrightnessProperty, value); }
        public double Contrast { get => (double)GetValue(ContrastProperty); set => SetValue(ContrastProperty, value); }
        public double Saturation { get => (double)GetValue(SaturationProperty); set => SetValue(SaturationProperty, value); }
        public double ScanlineStrength { get => (double)GetValue(ScanlineStrengthProperty); set => SetValue(ScanlineStrengthProperty, value); }
        public double LineCount { get => (double)GetValue(LineCountProperty); set => SetValue(LineCountProperty, value); }
        public void Refresh()
        {
            UpdateShaderValue(BrightnessProperty);
            UpdateShaderValue(ContrastProperty);
            UpdateShaderValue(SaturationProperty);
            UpdateShaderValue(GammaProperty);
            UpdateShaderValue(PhosphorStrengthProperty);
            UpdateShaderValue(ScanlineStrengthProperty);
            UpdateShaderValue(ScreenWidthProperty);
            UpdateShaderValue(ScreenHeightProperty);
            UpdateShaderValue(EffectiveWidthProperty);
            UpdateShaderValue(EffectiveHeightProperty);
            UpdateShaderValue(BeamWidthProperty);
            UpdateShaderValue(MaskTypeProperty);
            UpdateShaderValue(ScanlinePhaseProperty);
        }


    }
}
