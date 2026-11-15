// SPDX-License-Identifier: BSD-2-Clause
// ==========================================================================
// ADVENTURE: EFEITOS DE MAGIA modernos (desenhados pelo proprio cliente).
//  Tipo 1 = FURACAO de luz (curas): nasce pequeno e cresce; aneis girando que sobem e abrem para cima,
//           particulas em espiral e anel no chao. Cor e tamanho vem do servidor.
//  Tipo 2 = CORPO EM CHAMAS (Flamestrike): o proprio desenho do personagem e
//           redesenhado em fogo transparente subindo (ver MobileView.cs).
//
// Pacote proprio 0xBF sub 0xAD04 (18 bytes):
//   [tipo byte][alvo dword][R byte][G byte][B byte][tamanho byte][valor word][duracao word ms]
//   Sphere: SENDPACKET 0bf w18 w0AD04 <tipo> d<alvo> <r> <g> <b> <tamanho> w<valor> w<ms>
// ==========================================================================

using System;
using System.Collections.Generic;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.UI.Gumps
{
    public class AdventureFxGump : Gump
    {
        private sealed class Efeito
        {
            public uint Alvo;
            public Color Cor;
            public int Tamanho;
            public uint Inicio, Fim;
        }

        private static AdventureFxGump _instancia;
        private readonly List<Efeito> _efeitos = new List<Efeito>();

        public AdventureFxGump(World world) : base(world, 0, 0)
        {
            _instancia = this;
            CanMove = false;
            AcceptMouseInput = false;
            CanCloseWithRightClick = false;
            WantUpdateSize = false;
            X = 0;
            Y = 0;
            Width = 1;
            Height = 1;
        }

        public static void Receber(World world, int tipo, uint alvo, byte r, byte g, byte b, int tamanho, ushort valor, ushort ms)
        {
            if (ms == 0)
            {
                ms = 1500;
            }

            if (tipo == 2)
            {
                // Corpo em chamas: so marca o mobile; quem desenha e o MobileView
                Mobile m = world.Mobiles.Get(alvo);

                if (m != null)
                {
                    m.AdvFogoAte = Time.Ticks + ms;
                }

                return;
            }

            if (_instancia == null || _instancia.IsDisposed)
            {
                UIManager.Add(new AdventureFxGump(world));
            }

            _instancia._efeitos.Add(new Efeito
            {
                Alvo = alvo,
                Cor = new Color(r, g, b),
                Tamanho = Math.Max(1, Math.Min((int)tamanho, 4)),
                Inicio = Time.Ticks,
                Fim = Time.Ticks + ms
            });
        }

        private static Point Tela(Mobile m)
        {
            int wx = (int)(m.RealScreenPosition.X + m.Offset.X + 22);
            int wy = (int)(m.RealScreenPosition.Y + (m.Offset.Y - m.Offset.Z)) + 18;
            Point p = Client.Game.Scene.Camera.WorldToScreen(new Point(wx, wy));
            Camera camera = Client.Game.Scene.Camera;

            return new Point(p.X + camera.Bounds.X, p.Y + camera.Bounds.Y);
        }

        private static void Ponto(UltimaBatcher2D batcher, int x, int y, int tam, Color cor, float alpha)
        {
            if (alpha <= 0.01f)
            {
                return;
            }

            batcher.Draw(SolidColorTextureCache.GetTexture(cor), new Rectangle(x - tam / 2, y - tam / 2, tam, tam), ShaderHueTranslator.GetHueVector(0, false, Math.Min(alpha, 1f)));
        }

        public override bool Draw(UltimaBatcher2D batcher, int x, int y)
        {
            for (int i = _efeitos.Count - 1; i >= 0; i--)
            {
                Efeito e = _efeitos[i];

                if (Time.Ticks >= e.Fim)
                {
                    _efeitos.RemoveAt(i);
                    continue;
                }

                Mobile m = World.Mobiles.Get(e.Alvo);

                if (m == null)
                {
                    continue;
                }

                Point pe = Tela(m);   // pes do alvo na tela
                float t = (Time.Ticks - e.Inicio) / (float)(e.Fim - e.Inicio);   // 0 -> 1
                float sumir = t < 0.75f ? 1f : 1f - (t - 0.75f) / 0.25f;         // some no final
                float cresce = 0.3f + 0.7f * Math.Min(1f, t / 0.45f);           // comeca pequeno e vai crescendo
                float escala = (0.6f + 0.25f * e.Tamanho) * cresce;              // 1 = pequeno, 4 = gigante
                int altura = (int)(150 * escala);
                float giro = (Time.Ticks - e.Inicio) / 1000f * 7f;
                Color clara = Color.Lerp(e.Cor, Color.White, 0.55f);
                Color dourada = new Color(255, 240, 150);

                // Anel no chao abrindo
                int rc = (int)((25 + 70 * t) * escala);

                for (int a = 0; a < 48; a++)
                {
                    double ang = a / 48.0 * Math.PI * 2;
                    Ponto(batcher, pe.X + (int)(Math.Cos(ang) * rc), pe.Y + (int)(Math.Sin(ang) * rc * 0.35), 3, dourada, 0.8f * (1f - t));
                }

                // Aneis do funil girando e subindo (abre para cima)
                for (int k = 0; k < 9; k++)
                {
                    float fase = (k / 9f + t * 0.9f) % 1f;
                    int yy = pe.Y - (int)(fase * altura);
                    int raio = (int)((18 + fase * 70) * escala);
                    float alfa = (float)Math.Sin(Math.PI * fase) * 0.55f * sumir;

                    for (int a = 0; a < 36; a++)
                    {
                        double ang = a / 36.0 * Math.PI * 2 + giro;
                        Ponto(batcher, pe.X + (int)(Math.Cos(ang) * raio), yy + (int)(Math.Sin(ang) * raio * 0.28), 2, e.Cor, alfa);
                    }
                }

                // Particulas em espiral (3 bracos)
                for (int braco = 0; braco < 3; braco++)
                {
                    for (int p = 0; p < 26; p++)
                    {
                        float fase = (p / 26f + t * 1.1f) % 1f;
                        double ang = fase * Math.PI * 6 + braco * 2 * Math.PI / 3 + giro;
                        int raio = (int)((18 + fase * 70) * escala);
                        int px = pe.X + (int)(Math.Cos(ang) * raio);
                        int py = pe.Y - (int)(fase * altura) + (int)(Math.Sin(ang) * raio * 0.28);
                        Color c = p % 3 == 0 ? Color.White : (p % 3 == 1 ? clara : dourada);
                        int tam = Math.Sin(ang) > 0 ? 4 : 3;   // na frente fica maior
                        Ponto(batcher, px, py, tam, c, (float)Math.Sin(Math.PI * fase) * sumir);
                    }
                }
            }

            return true;
        }
    }
}
