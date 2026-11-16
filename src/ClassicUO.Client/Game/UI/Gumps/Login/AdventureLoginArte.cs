// SPDX-License-Identifier: BSD-2-Clause
// ==========================================================================
// ADVENTURE: ARTE DA TELA DE LOGIN ("Reino ao Amanhecer").
//
// As imagens ficam na pasta do cliente, em Data\Client (vem no pacote e no launcher):
//   adventure_fundo.jpg  = fundo de todas as telas antes do jogo (login, servidor, personagens)
//   adventure_login.jpg  = mesma arte com o quadro de login dourado (CONTA / SENHA) ja desenhado
// As duas tem 1280x960 (o dobro da tela de login de 640x480, para ficar nitida).
// Se a imagem nao existir, nada e desenhado (fica o fundo antigo do UO).
//
// AdventureImagem      = desenha uma dessas imagens esticada no tamanho do controle.
// AdventureBotaoEntrar = botao dourado "ENTRAR" (brilha com o mouse em cima).
// AdventureMarcador    = caixinha de marcar moderna (borda dourada + check) com texto liso.
// AdventureSlider      = barra de volume moderna (trilho escuro, parte cheia dourada, bolinha).
// AdventureRotulo      = texto liso (fonte Roboto), no lugar da fonte antiga do UO.
// AdventureBarraTitulo = barra de titulo propria (igual a do launcher): arrasta a janela,
//                        botoes modernos de minimizar e fechar. So nas telas antes do jogo.
// ==========================================================================

using System;
using System.Collections.Generic;
using System.IO;
using ClassicUO.Assets;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Input;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ClassicUO.Game.UI.Gumps.Login
{
    public class AdventureImagem : Control
    {
        public const string FUNDO = "adventure_fundo.jpg";
        public const string LOGIN = "adventure_login.jpg";

        // carrega cada imagem uma vez so
        private static readonly Dictionary<string, Texture2D> _cache = new Dictionary<string, Texture2D>();

        private readonly string _arquivo;

        public AdventureImagem(string arquivo, int largura = 640, int altura = 480)
        {
            _arquivo = arquivo;
            Width = largura;
            Height = altura;
            AcceptMouseInput = false;
            AcceptKeyboardInput = false;
        }

        // true se a imagem existe na pasta Data\Client
        public static bool Existe(string arquivo)
        {
            return Textura(arquivo) != null;
        }

        private static Texture2D Textura(string arquivo)
        {
            if (_cache.TryGetValue(arquivo, out Texture2D t))
            {
                return t;
            }

            t = null;

            try
            {
                string caminho = Path.Combine(CUOEnviroment.ExecutablePath, "Data", "Client", arquivo);

                if (File.Exists(caminho))
                {
                    using (FileStream fs = File.OpenRead(caminho))
                    {
                        t = Texture2D.FromStream(Client.Game.GraphicsDevice, fs);
                    }
                }
            }
            catch
            {
                t = null;   // imagem estragada: usa o fundo antigo
            }

            _cache[arquivo] = t;

            return t;
        }

        public override bool Draw(UltimaBatcher2D batcher, int x, int y)
        {
            Texture2D t = Textura(_arquivo);

            if (t != null && !t.IsDisposed)
            {
                batcher.Draw(t, new Rectangle(x, y, Width, Height), ShaderHueTranslator.GetHueVector(0, false, Alpha));
            }

            return base.Draw(batcher, x, y);
        }
    }

    public class AdventureBotaoEntrar : Control
    {
        private readonly Action _aoClicar;
        private readonly TextBox _texto;

        public AdventureBotaoEntrar(int x, int y, int largura, int altura, string texto, Action aoClicar)
        {
            X = x;
            Y = y;
            Width = largura;
            Height = altura;
            _aoClicar = aoClicar;
            AcceptMouseInput = true;
            _texto = TextBox.GetOne(texto, EmbeddedFontNames.ROBOTO_BOLD, 16, new Color(40, 25, 8), TextBox.RTLOptions.DefaultCentered(largura));
        }

        public override void OnMouseUp(int x, int y, MouseButtonType button)
        {
            if (button == MouseButtonType.Left)
            {
                _aoClicar?.Invoke();
            }

            base.OnMouseUp(x, y, button);
        }

        public override bool Draw(UltimaBatcher2D batcher, int x, int y)
        {
            bool sobre = MouseIsOver;

            // degrade dourado: claro em cima, escuro embaixo (em faixas)
            Color cima = sobre ? new Color(255, 228, 150) : new Color(245, 212, 125);
            Color baixo = sobre ? new Color(205, 155, 60) : new Color(185, 135, 45);
            const int FAIXAS = 8;

            for (int i = 0; i < FAIXAS; i++)
            {
                float t = i / (float)(FAIXAS - 1);
                int y0 = y + Height * i / FAIXAS;
                int y1 = y + Height * (i + 1) / FAIXAS;
                Ret(batcher, x, y0, Width, y1 - y0, Color.Lerp(cima, baixo, t));
            }

            batcher.DrawRectangle(SolidColorTextureCache.GetTexture(new Color(90, 65, 25)), x, y, Width, Height, ShaderHueTranslator.GetHueVector(0, false, 1f));
            _texto.Draw(batcher, x, y + (Height - _texto.Height) / 2);

            return base.Draw(batcher, x, y);
        }

        private static void Ret(UltimaBatcher2D batcher, int x, int y, int w, int h, Color cor)
        {
            if (w > 0 && h > 0)
            {
                batcher.Draw(SolidColorTextureCache.GetTexture(cor), new Rectangle(x, y, w, h), ShaderHueTranslator.GetHueVector(0, false, 1f));
            }
        }

        public override void Dispose()
        {
            _texto?.Dispose();
            base.Dispose();
        }
    }

    public class AdventureRotulo : Control
    {
        private TextBox _txt;
        private string _texto;
        private readonly int _tam;
        private readonly Color _cor;
        private readonly bool _negrito;

        public AdventureRotulo(int x, int y, string texto, int tamanho, Color cor, bool negrito = false)
        {
            X = x;
            Y = y;
            _tam = tamanho;
            _cor = cor;
            _negrito = negrito;
            AcceptMouseInput = false;
            Texto = texto;
        }

        public string Texto
        {
            get => _texto;
            set
            {
                if (_texto == value && _txt != null)
                {
                    return;
                }

                _texto = value;
                _txt?.Dispose();
                _txt = TextBox.GetOne(value ?? "", _negrito ? EmbeddedFontNames.ROBOTO_BOLD : EmbeddedFontNames.ROBOTO, _tam, _cor, TextBox.RTLOptions.Default(400));
                Width = _txt.Width;
                Height = _txt.Height;
            }
        }

        public override bool Draw(UltimaBatcher2D batcher, int x, int y)
        {
            _txt?.Draw(batcher, x, y);

            return base.Draw(batcher, x, y);
        }

        public override void Dispose()
        {
            _txt?.Dispose();
            base.Dispose();
        }
    }

    public class AdventureMarcador : Control
    {
        private static readonly Color OURO = new Color(212, 170, 80);
        private readonly TextBox _txt;
        private bool _marcado;

        public event Action<bool> Mudou;

        public AdventureMarcador(int x, int y, string texto, bool marcado)
        {
            X = x;
            Y = y;
            _marcado = marcado;
            AcceptMouseInput = true;
            _txt = TextBox.GetOne(texto, EmbeddedFontNames.ROBOTO, 13, new Color(235, 228, 210), TextBox.RTLOptions.Default(300));
            Width = 18 + _txt.Width;
            Height = Math.Max(14, _txt.Height);
        }

        public bool Marcado
        {
            get => _marcado;
            set => _marcado = value;
        }

        public override void OnMouseUp(int x, int y, MouseButtonType button)
        {
            if (button == MouseButtonType.Left)
            {
                _marcado = !_marcado;
                Mudou?.Invoke(_marcado);
            }

            base.OnMouseUp(x, y, button);
        }

        public override bool Draw(UltimaBatcher2D batcher, int x, int y)
        {
            int cy = y + (Height - 12) / 2;
            Ret(batcher, x, cy, 12, 12, new Color(18, 15, 12), 0.95f);
            batcher.DrawRectangle(SolidColorTextureCache.GetTexture(MouseIsOver ? new Color(255, 215, 120) : OURO), x, cy, 12, 12, ShaderHueTranslator.GetHueVector(0, false, 1f));

            if (_marcado)
            {
                Ret(batcher, x + 3, cy + 3, 6, 6, OURO, 1f);   // quadradinho dourado = marcado
            }

            _txt.Draw(batcher, x + 18, y + (Height - _txt.Height) / 2);

            return base.Draw(batcher, x, y);
        }

        internal static void Ret(UltimaBatcher2D batcher, int x, int y, int w, int h, Color cor, float a)
        {
            if (w > 0 && h > 0)
            {
                batcher.Draw(SolidColorTextureCache.GetTexture(cor), new Rectangle(x, y, w, h), ShaderHueTranslator.GetHueVector(0, false, a));
            }
        }

        public override void Dispose()
        {
            _txt?.Dispose();
            base.Dispose();
        }
    }

    public class AdventureSlider : Control
    {
        private static readonly Color OURO = new Color(212, 170, 80);
        private int _valor;
        private bool _arrastando;

        public event Action<int> Mudou;

        public AdventureSlider(int x, int y, int largura, int valor)
        {
            X = x;
            Y = y;
            Width = largura;
            Height = 14;
            _valor = Math.Max(0, Math.Min(100, valor));
            AcceptMouseInput = true;
        }

        public int Valor => _valor;

        public override void OnMouseDown(int x, int y, MouseButtonType button)
        {
            if (button == MouseButtonType.Left)
            {
                _arrastando = true;
                Mover(x);
            }

            base.OnMouseDown(x, y, button);
        }

        public override void OnMouseUp(int x, int y, MouseButtonType button)
        {
            _arrastando = false;
            base.OnMouseUp(x, y, button);
        }

        public override void Update()
        {
            base.Update();

            if (_arrastando)
            {
                if (!Mouse.LButtonPressed)
                {
                    _arrastando = false;
                }
                else
                {
                    Mover(Mouse.Position.X - ScreenCoordinateX);
                }
            }
        }

        private void Mover(int x)
        {
            int v = (int)Math.Round(Math.Max(0, Math.Min(Width, x)) * 100f / Width);

            if (v != _valor)
            {
                _valor = v;
                Mudou?.Invoke(v);
            }
        }

        public override bool Draw(UltimaBatcher2D batcher, int x, int y)
        {
            int cy = y + Height / 2;
            int cheio = Width * _valor / 100;
            AdventureMarcador.Ret(batcher, x, cy - 2, Width, 4, new Color(25, 21, 17), 0.95f);   // trilho
            AdventureMarcador.Ret(batcher, x, cy - 2, cheio, 4, OURO, 1f);                       // parte cheia
            Color bola = MouseIsOver || _arrastando ? new Color(255, 225, 140) : new Color(235, 200, 110);
            AdventureMarcador.Ret(batcher, x + cheio - 4, cy - 5, 9, 10, bola, 1f);              // bolinha
            batcher.DrawRectangle(SolidColorTextureCache.GetTexture(new Color(90, 65, 25)), x + cheio - 4, cy - 5, 9, 10, ShaderHueTranslator.GetHueVector(0, false, 1f));

            return base.Draw(batcher, x, y);
        }
    }

    // Barra de titulo propria nas telas antes do jogo (login, servidor, personagens).
    // A janela do Windows fica sem borda; esta barra arrasta a janela e tem _ e X modernos.
    public class AdventureBarraTitulo : Gump
    {
        public const int ALT = 26;
        private static readonly Color FUNDO = new Color(14, 11, 8);
        private static readonly Color OURO = new Color(212, 170, 80);

        private readonly TextBox _titulo, _min, _fechar;
        private bool _arrastando;
        private float _offX, _offY;

        public AdventureBarraTitulo(World world) : base(world, 0, 0)
        {
            Width = 640;
            Height = ALT;
            CanMove = false;
            AcceptMouseInput = true;
            CanCloseWithRightClick = false;
            CanCloseWithEsc = false;
            LayerOrder = UILayer.Over;   // sempre por cima das telas de login
            _titulo = TextBox.GetOne("Adventure UO   |   Beta 0.0.1", EmbeddedFontNames.ROBOTO_BOLD, 13, new Color(235, 195, 110), TextBox.RTLOptions.Default(400));
            _min = TextBox.GetOne("_", EmbeddedFontNames.ROBOTO_BOLD, 14, new Color(235, 220, 190), TextBox.RTLOptions.DefaultCentered(22));
            _fechar = TextBox.GetOne("X", EmbeddedFontNames.ROBOTO_BOLD, 13, new Color(235, 220, 190), TextBox.RTLOptions.DefaultCentered(22));
        }

        private Rectangle RetMin => new Rectangle(Width - 58, 3, 22, 20);
        private Rectangle RetFechar => new Rectangle(Width - 30, 3, 22, 20);

        public override void OnMouseDown(int x, int y, MouseButtonType button)
        {
            if (button == MouseButtonType.Left && !RetMin.Contains(x, y) && !RetFechar.Contains(x, y))
            {
                // comeca a arrastar a janela: guarda onde o mouse pegou
                SDL3.SDL.SDL_GetGlobalMouseState(out float gx, out float gy);
                SDL3.SDL.SDL_GetWindowPosition(Client.Game.Window.Handle, out int wx, out int wy);
                _offX = gx - wx;
                _offY = gy - wy;
                _arrastando = true;
            }

            base.OnMouseDown(x, y, button);
        }

        public override void OnMouseUp(int x, int y, MouseButtonType button)
        {
            _arrastando = false;

            if (button == MouseButtonType.Left)
            {
                if (RetMin.Contains(x, y))
                {
                    SDL3.SDL.SDL_MinimizeWindow(Client.Game.Window.Handle);
                }
                else if (RetFechar.Contains(x, y))
                {
                    Client.Game.Exit();
                }
            }

            base.OnMouseUp(x, y, button);
        }

        public override void Update()
        {
            base.Update();

            Width = Math.Max(200, ScaleHelper.LogicalWindowWidth);

            if (_arrastando)
            {
                if (!Mouse.LButtonPressed)
                {
                    _arrastando = false;
                }
                else
                {
                    SDL3.SDL.SDL_GetGlobalMouseState(out float gx, out float gy);
                    SDL3.SDL.SDL_SetWindowPosition(Client.Game.Window.Handle, (int)(gx - _offX), (int)(gy - _offY));
                }
            }
        }

        public override bool Draw(UltimaBatcher2D batcher, int x, int y)
        {
            AdventureMarcador.Ret(batcher, x, y, Width, ALT, FUNDO, 0.96f);
            AdventureMarcador.Ret(batcher, x, y + ALT - 1, Width, 1, OURO, 0.9f);
            _titulo.Draw(batcher, x + 10, y + (ALT - _titulo.Height) / 2);

            int mx = Mouse.Position.X - x, my = Mouse.Position.Y - y;
            bool sobre = UIManager.MouseOverControl == this;
            Botao(batcher, x, y, RetMin, sobre && RetMin.Contains(mx, my), new Color(70, 58, 40), _min);
            Botao(batcher, x, y, RetFechar, sobre && RetFechar.Contains(mx, my), new Color(170, 35, 30), _fechar);

            return base.Draw(batcher, x, y);
        }

        private static void Botao(UltimaBatcher2D batcher, int x, int y, Rectangle r, bool sobre, Color corSobre, TextBox t)
        {
            AdventureMarcador.Ret(batcher, x + r.X, y + r.Y, r.Width, r.Height, sobre ? corSobre : new Color(32, 26, 20), 0.95f);
            batcher.DrawRectangle(SolidColorTextureCache.GetTexture(new Color(110, 85, 40)), x + r.X, y + r.Y, r.Width, r.Height, ShaderHueTranslator.GetHueVector(0, false, 1f));
            t.Draw(batcher, x + r.X, y + r.Y + (r.Height - t.Height) / 2);
        }

        public override void Dispose()
        {
            _titulo?.Dispose();
            _min?.Dispose();
            _fechar?.Dispose();
            base.Dispose();
        }
    }
}
