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
// ==========================================================================

using System;
using System.Collections.Generic;
using System.IO;
using ClassicUO.Assets;
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
}
