// SPDX-License-Identifier: BSD-2-Clause
// ==========================================================================
// ADVENTURE: aviso "aperte a tecla" do auxiliar (modo esperando tecla, pacote 0xAD08).
// So mostra o aviso; quem captura a tecla e o AdventureAuxiliar.
// ==========================================================================

using System;
using ClassicUO.Assets;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.UI.Gumps
{
    public class AdventureAuxTeclaGump : Gump
    {
        private const int LARG = 420, ALT = 74;

        private static AdventureAuxTeclaGump _instancia;
        private TextBox _titulo, _texto;

        private AdventureAuxTeclaGump(World world, int botao) : base(world, 0, 0)
        {
            _instancia = this;
            CanMove = false;
            AcceptMouseInput = false;
            CanCloseWithRightClick = false;
            WantUpdateSize = false;
            Width = LARG;
            Height = ALT;

            _titulo = TextBox.GetOne("AUXILIAR - BOTAO " + botao, EmbeddedFontNames.ROBOTO_BOLD, 15, new Color(235, 190, 60), TextBox.RTLOptions.DefaultCentered(LARG));
            _texto = TextBox.GetOne("Aperte a tecla ou botao do mouse (pode usar Ctrl, Alt e Shift)\nEsc cancela  -  Delete apaga a tecla deste botao", EmbeddedFontNames.ROBOTO, 13, Color.White, TextBox.RTLOptions.DefaultCentered(LARG));
        }

        public static void Mostrar(World world, int botao)
        {
            Fechar();
            UIManager.Add(new AdventureAuxTeclaGump(world, botao));
        }

        public static void Fechar()
        {
            if (_instancia != null && !_instancia.IsDisposed)
            {
                _instancia.Dispose();
            }

            _instancia = null;
        }

        public override void Update()
        {
            base.Update();

            if (IsDisposed)
            {
                return;
            }

            if (!AdventureAuxiliar.Capturando)
            {
                AdventureAuxiliar.CancelarCaptura();   // passou o tempo: cancela sozinho

                return;
            }

            // centralizado no alto da tela
            X = (ScaleHelper.LogicalWindowWidth - LARG) / 2;
            Y = 90;
        }

        public override bool Draw(UltimaBatcher2D batcher, int x, int y)
        {
            if (!base.Draw(batcher, x, y))
            {
                return false;
            }

            float pulso = 0.75f + 0.25f * (float)Math.Sin(Time.Ticks / 1000.0 * Math.PI * 2);

            batcher.Draw(SolidColorTextureCache.GetTexture(new Color(14, 14, 18)), new Rectangle(x, y, LARG, ALT), ShaderHueTranslator.GetHueVector(0, false, 0.9f));
            batcher.DrawRectangle(SolidColorTextureCache.GetTexture(new Color(235, 190, 60)), x, y, LARG, ALT, ShaderHueTranslator.GetHueVector(0, false, pulso));
            _titulo.Draw(batcher, x, y + 8);
            _texto.Draw(batcher, x, y + 32);

            return true;
        }

        public override void Dispose()
        {
            _titulo?.Dispose();
            _texto?.Dispose();

            if (_instancia == this)
            {
                _instancia = null;
            }

            base.Dispose();
        }
    }
}
