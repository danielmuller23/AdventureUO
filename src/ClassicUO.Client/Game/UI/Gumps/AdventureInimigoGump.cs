// SPDX-License-Identifier: BSD-2-Clause
// ==========================================================================
// ADVENTURE: MARCA DE INIMIGO DO AUXILIAR (circulo vermelho no chao + etiqueta).
//
// Pacote proprio 0xBF sub 0xAD07 (9 bytes no total):
//   [UID dword]          UID 0 = apagar a marca
//   Sphere: SENDPACKET 0bf w09 w0AD07 d<uid>
//
// - Circulo vermelho pulsando no chao, debaixo do mobile (desenhado junto com o
//   personagem no MobileView, por baixo do corpo).
// - Etiqueta "INIMIGO: nome" em cima da cabeca.
// - Se o mobile sair da tela, a marca some e volta sozinha quando ele aparecer de novo.
// - So existe 1 inimigo marcado por vez (um pacote novo troca a marca).
// ==========================================================================

using System;
using ClassicUO.Assets;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ClassicUO.Game.UI.Gumps
{
    public class AdventureInimigoGump : Gump
    {
        public static uint Uid;   // inimigo marcado (0 = nenhum)

        private static AdventureInimigoGump _instancia;
        private static Texture2D _anel;
        private static readonly Lazy<BlendState> _blend = new Lazy<BlendState>(() => new BlendState
        {
            ColorSourceBlend = Blend.SourceAlpha,
            ColorDestinationBlend = Blend.InverseSourceAlpha,
            AlphaSourceBlend = Blend.SourceAlpha,
            AlphaDestinationBlend = Blend.InverseSourceAlpha
        });

        private TextBox _etiqueta;
        private string _nome;

        public AdventureInimigoGump(World world) : base(world, 0, 0)
        {
            _instancia = this;
            CanMove = false;
            AcceptMouseInput = false;
            CanCloseWithRightClick = false;
            WantUpdateSize = false;
            Width = 1;
            Height = 1;
        }

        // ---------- pacote ----------
        public static void Receber(World world, uint uid)
        {
            Uid = uid;

            if (uid == 0)
            {
                _instancia?.Dispose();   // apagou a marca

                return;
            }

            if (_instancia == null || _instancia.IsDisposed)
            {
                UIManager.Add(new AdventureInimigoGump(world));
            }
        }

        // ---------- circulo no chao (chamado pelo MobileView, por baixo do corpo) ----------
        public static void DesenhaAnel(UltimaBatcher2D batcher, int x, int y, float depth)
        {
            if (_anel == null || _anel.IsDisposed)
            {
                const int W = 96, H = 48;   // elipse achatada (perspectiva do UO)
                var dados = new Color[W * H];

                for (int yy = 0; yy < H; yy++)
                {
                    for (int xx = 0; xx < W; xx++)
                    {
                        float dx = (xx - W / 2 + 0.5f) / (W / 2f);
                        float dy = (yy - H / 2 + 0.5f) / (H / 2f);
                        float d = (float)Math.Sqrt(dx * dx + dy * dy);   // 0 centro, 1 borda

                        float a = 0f;

                        if (d <= 1f)
                        {
                            float borda = 1f - Math.Abs(d - 0.86f) / 0.14f;   // anel forte perto da borda
                            float miolo = 0.22f * (1f - d);                   // miolo bem fraco
                            a = Math.Max(0f, Math.Max(borda, miolo));
                        }

                        dados[yy * W + xx] = new Color(235, 30, 25, (int)(255 * Math.Min(1f, a)));
                    }
                }

                _anel = new Texture2D(batcher.GraphicsDevice, W, H, false, SurfaceFormat.Color);
                _anel.SetData(dados);
            }

            float pulso = 0.75f + 0.25f * (float)Math.Sin(Time.Ticks / 1000.0 * Math.PI * 2 * 0.8);   // pulsa devagar
            Vector3 hue = ShaderHueTranslator.GetHueVector(0, false, pulso);

            batcher.SetBlendState(_blend.Value);
            batcher.Draw(_anel, new Vector2(x - _anel.Width / 2, y - _anel.Height / 2), null, hue, 0f, Vector2.Zero, 1f, SpriteEffects.None, depth);
            batcher.SetBlendState(null);
        }

        // ---------- etiqueta em cima da cabeca ----------
        private bool CimaDaCabeca(Mobile m, out Point tela)
        {
            Client.Game.UO.Animations.GetAnimationDimensions(
                m.AnimIndex,
                m.GetGraphicForAnimation(),
                0,
                0,
                m.IsMounted,
                0,
                out int centerX,
                out int centerY,
                out int width,
                out int height
            );

            int wx = (int)(m.RealScreenPosition.X + m.Offset.X + 22);
            int wy = (int)(m.RealScreenPosition.Y + (m.Offset.Y - m.Offset.Z) - (height + centerY + 14));

            Camera camera = Client.Game.Scene.Camera;
            Point p = camera.WorldToScreen(new Point(wx, wy));
            tela = new Point(p.X + camera.Bounds.X, p.Y + camera.Bounds.Y);

            return true;
        }

        public override bool Draw(UltimaBatcher2D batcher, int x, int y)
        {
            if (Uid == 0)
            {
                return true;
            }

            Mobile m = World.Mobiles.Get(Uid);

            if (m == null || m.IsDestroyed || m.Distance > World.ClientViewRange)
            {
                return true;   // fora da tela: esconde ate voltar
            }

            string nome = string.IsNullOrEmpty(m.Name) ? "???" : m.Name;

            if (_etiqueta == null || _nome != nome)
            {
                _nome = nome;
                _etiqueta?.Dispose();
                _etiqueta = TextBox.GetOne("INIMIGO: " + nome, EmbeddedFontNames.ROBOTO_BOLD, 13, new Color(255, 90, 80), TextBox.RTLOptions.DefaultCenterStroked(220));
            }

            CimaDaCabeca(m, out Point p);

            // fundo escuro com borda vermelha, do tamanho do texto
            int tw = Math.Min(220, _etiqueta.MeasuredSize.X + 14);
            int th = _etiqueta.Height + 4;
            int bx = p.X - tw / 2, by = p.Y - th;

            batcher.Draw(SolidColorTextureCache.GetTexture(new Color(15, 8, 8)), new Rectangle(bx, by, tw, th), ShaderHueTranslator.GetHueVector(0, false, 0.75f));
            batcher.DrawRectangle(SolidColorTextureCache.GetTexture(new Color(200, 40, 35)), bx, by, tw, th, ShaderHueTranslator.GetHueVector(0, false, 0.9f));
            _etiqueta.Draw(batcher, p.X - 110, by + 2);

            return true;
        }

        public override void Dispose()
        {
            _etiqueta?.Dispose();

            if (_instancia == this)
            {
                _instancia = null;
            }

            base.Dispose();
        }
    }
}
