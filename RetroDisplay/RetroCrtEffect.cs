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
    }
}
