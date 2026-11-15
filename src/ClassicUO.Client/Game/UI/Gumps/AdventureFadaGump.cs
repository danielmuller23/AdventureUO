// SPDX-License-Identifier: BSD-2-Clause
// ==========================================================================
// ADVENTURE: FADA do player = ESPIRITO DE LUZ. Desenhado inteiro pelo cliente
// (nao usa arte do art.mul). Nucleo branco + halo em degrade na cor do nivel +
// arcos de luz girando + faiscas + rastro quando anda.
//  Cores (degrade A): nivel 1 azul, 2 verde, 3 branco, 4 laranja, 5 dourado.
//  Estados:
//   0 parada      - flutua acima da cabeca do dono
//   1 cura        - sobe em espiral em volta do dono (brilho verde)
//   2 ataque      - voa ate o mob, da voltas nele e volta
//   3 exploracao  - voa ate o item achado, fica brilhando e volta
//   4 especial    - numero do especial vem no campo "arte" (1 a 5)
//   5 evolucao    - sobe girando e explode na cor nova
//  Cada player tem a sua fada e os outros tambem veem (uma por dono).
//
// Pacotes 0xBF:
//   AD03 (15 bytes, fada do proprio player): [nivel][estado][arte w][cor w][alvo d]
//   AD05 (19 bytes, fada de qualquer player): [dono d][nivel][estado][arte w][cor w][alvo d]
//   Sphere: SENDPACKET 0bf w19 w0AD05 d<dono> <nivel> <estado> w<especial> w0 d<alvo>
// ==========================================================================

using System;
using System.Collections.Generic;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ClassicUO.Game.UI.Gumps
{
    public class AdventureFadaGump : Gump
    {
        // ---- tempos (ms) ----
        private const uint T_CURA = 1600, T_ATAQUE = 1400, T_EXPLORA = 3000, T_ESPECIAL = 1700, T_PETALAS = 5000, T_EVOLUCAO = 2200;

        // ---- cores do degrade A (uma por nivel) ----
        private static readonly Color[] COR_NIVEL =
        {
            new Color(110, 180, 255),   // 1 azul
            new Color(110, 255, 150),   // 2 verde
            new Color(235, 240, 255),   // 3 branco
            new Color(255, 150, 60),    // 4 laranja
            new Color(255, 210, 80)     // 5 dourado
        };

        private static readonly Dictionary<uint, AdventureFadaGump> _fadas = new Dictionary<uint, AdventureFadaGump>();
        private static readonly Dictionary<uint, Texture2D> _halos = new Dictionary<uint, Texture2D>();

        private readonly uint _dono;
        private int _nivel;
        private int _estado, _especial;
        private uint _inicioEstado, _fimEstado, _alvo;
        private int _nivelAntes = 1;

        // rastro: ultimas posicoes na tela
        private readonly Point[] _rastro = new Point[14];
        private int _rastroN;
        private uint _rastroTick;

        public AdventureFadaGump(World world, uint dono) : base(world, 0, 0)
        {
            _dono = dono;
            CanMove = false;
            AcceptMouseInput = false;
            CanCloseWithRightClick = false;
            WantUpdateSize = false;
            Width = 1;
            Height = 1;
        }

        // ---------- recebe do servidor ----------
        public static void Receber(World world, int nivel, int estado, ushort arte, ushort cor, uint alvo)
        {
            if (world.Player != null)
            {
                Receber(world, world.Player.Serial, nivel, estado, arte, cor, alvo);
            }
        }

        public static void Receber(World world, uint dono, int nivel, int estado, ushort arte, ushort cor, uint alvo)
        {
            if (!_fadas.TryGetValue(dono, out AdventureFadaGump fada) || fada.IsDisposed)
            {
                if (nivel <= 0)
                {
                    return;
                }

                fada = new AdventureFadaGump(world, dono);
                _fadas[dono] = fada;
                UIManager.Add(fada);
            }

            fada.Definir(nivel, estado, arte, alvo);
        }

        private void Definir(int nivel, int estado, ushort especial, uint alvo)
        {
            if (estado == 5)
            {
                _nivelAntes = Math.Max(1, _nivel);
            }

            _nivel = nivel;

            if (estado > 0)
            {
                _estado = estado;
                _especial = especial;
                _alvo = alvo;
                _inicioEstado = Time.Ticks;
                uint dur = estado switch
                {
                    1 => T_CURA,
                    2 => T_ATAQUE,
                    3 => T_EXPLORA,
                    4 => especial == 4 ? T_PETALAS : T_ESPECIAL,
                    5 => T_EVOLUCAO,
                    _ => 1000
                };
                _fimEstado = Time.Ticks + dur;
            }
        }

        public override void Dispose()
        {
            if (_fadas.TryGetValue(_dono, out AdventureFadaGump f) && f == this)
            {
                _fadas.Remove(_dono);
            }

            base.Dispose();
        }

        // ---------- texturas de halo em degrade (branco -> cor -> transparente) ----------
        private static Texture2D Halo(Color cor)
        {
            if (_halos.TryGetValue(cor.PackedValue, out Texture2D tex))
            {
                return tex;
            }

            const int N = 64;
            var dados = new Color[N * N];
            Color escuro = Color.Lerp(cor, Color.Black, 0.5f);

            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    float dx = (x - N / 2 + 0.5f) / (N / 2f), dy = (y - N / 2 + 0.5f) / (N / 2f);
                    float t = Math.Min(1f, (float)Math.Sqrt(dx * dx + dy * dy));       // 0 centro -> 1 borda
                    Color c = t < 0.5f ? Color.Lerp(Color.White, cor, t / 0.5f) : Color.Lerp(cor, escuro, (t - 0.5f) / 0.5f);
                    float a = (float)Math.Pow(1f - t, 1.6);                               // some na borda
                    dados[y * N + x] = new Color((byte)(c.R * a), (byte)(c.G * a), (byte)(c.B * a), (byte)(255 * a)); // pre-multiplicado
                }
            }

            tex = new Texture2D(Client.Game.GraphicsDevice, N, N, false, SurfaceFormat.Color);
            tex.SetData(dados);
            _halos[cor.PackedValue] = tex;

            return tex;
        }

        private static void Bola(UltimaBatcher2D b, int x, int y, int raio, Color cor, float alfa)
        {
            if (alfa <= 0.01f || raio <= 0)
            {
                return;
            }

            b.Draw(Halo(cor), new Rectangle(x - raio, y - raio, raio * 2, raio * 2), ShaderHueTranslator.GetHueVector(0, false, Math.Min(1f, alfa)));
        }

        private static void Ponto(UltimaBatcher2D b, int x, int y, int tam, Color cor, float alfa)
        {
            if (alfa <= 0.01f || tam <= 0)
            {
                return;
            }

            b.Draw(SolidColorTextureCache.GetTexture(cor), new Rectangle(x - tam / 2, y - tam / 2, tam, tam), ShaderHueTranslator.GetHueVector(0, false, Math.Min(1f, alfa)));
        }

        private static void Linha(UltimaBatcher2D b, int x1, int y1, int x2, int y2, int tam, Color cor, float alfa)
        {
            int passos = Math.Max(1, Math.Max(Math.Abs(x2 - x1), Math.Abs(y2 - y1)) / Math.Max(1, tam));

            for (int i = 0; i <= passos; i++)
            {
                float f = i / (float)passos;
                Ponto(b, x1 + (int)((x2 - x1) * f), y1 + (int)((y2 - y1) * f), tam, cor, alfa);
            }
        }

        private static float Rnd(int s)
        {
            uint h = (uint)(s * 374761393 + 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;

            return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
        }

        // Ponto na tela (ja com camera) em cima de um mobile ou item
        private static Point Tela(GameObject m, int dx, int dy)
        {
            int wx = (int)(m.RealScreenPosition.X + m.Offset.X + 22) + dx;
            int wy = (int)(m.RealScreenPosition.Y + (m.Offset.Y - m.Offset.Z)) + dy;
            Camera camera = Client.Game.Scene.Camera;
            Point p = camera.WorldToScreen(new Point(wx, wy));

            return new Point(p.X + camera.Bounds.X, p.Y + camera.Bounds.Y);
        }

        private static Color CorDoNivel(int nivel) => COR_NIVEL[Math.Max(1, Math.Min(5, nivel)) - 1];

        // ---------- desenho ----------
        public override bool Draw(UltimaBatcher2D batcher, int x, int y)
        {
            Mobile dono = World.Mobiles.Get(_dono);

            if (_nivel <= 0 || dono == null)
            {
                return false;
            }

            if (_estado > 0 && Time.Ticks >= _fimEstado)
            {
                _estado = 0;
            }

            double t = Time.Ticks / 1000.0;
            float f = _estado > 0 ? (Time.Ticks - _inicioEstado) / (float)(_fimEstado - _inicioEstado) : 0f;   // 0 -> 1 no estado
            Color cor = CorDoNivel(_nivel);
            int tam = 9 + _nivel;                                     // raio do halo cresce com o nivel

            // casa: acima da cabeca, flutuando
            int bobY = (int)(Math.Sin(t * 2.6) * 4);
            int bobX = (int)(Math.Cos(t * 1.3) * 3);
            Point pe = Tela(dono, 0, 0);
            Point casa = Tela(dono, bobX, -(dono.IsMounted ? 100 : 82) + bobY);
            Point pos = casa;

            batcher.SetBlendState(BlendState.Additive);   // luz somada: brilho bonito

            switch (_estado)
            {
                case 1: pos = Cura(batcher, dono, pe, f, t); break;
                case 2: pos = VaiERodeia(casa, f, t, 0.3f, 0.4f, 2f, 30); break;
                case 3: pos = Explora(batcher, casa, f, t); break;
                case 4: pos = Especial(batcher, dono, casa, pe, f, t, cor); break;
                case 5: pos = Evolucao(batcher, casa, f, t, ref cor, ref tam); break;
            }

            // rastro (guarda posicoes a cada ~30ms)
            if (Time.Ticks - _rastroTick > 30)
            {
                _rastroTick = Time.Ticks;
                Array.Copy(_rastro, 0, _rastro, 1, _rastro.Length - 1);
                _rastro[0] = pos;
                _rastroN = Math.Min(_rastroN + 1, _rastro.Length);
            }

            for (int i = 1; i < _rastroN; i++)
            {
                Point r = _rastro[i];

                if (Math.Abs(r.X - pos.X) + Math.Abs(r.Y - pos.Y) < 3)
                {
                    continue;   // parada: sem rastro
                }

                float k = 1f - i / (float)_rastroN;
                Bola(batcher, r.X, r.Y, Math.Max(2, (int)(tam * 0.55f * k)), Color.Lerp(cor, Color.Black, 0.25f * (1 - k)), 0.85f * k);
            }

            // ---- o espirito ----
            float pulsa = 1f + 0.08f * (float)Math.Sin(t * 5);
            Bola(batcher, pos.X, pos.Y, (int)(tam * 2.2f * pulsa), cor, 0.85f);            // halo grande
            Bola(batcher, pos.X, pos.Y, (int)(tam * 1.1f), cor, 1f);                        // halo medio
            Bola(batcher, pos.X, pos.Y, Math.Max(3, tam / 2), Color.White, 1f);              // nucleo

            // 3 arcos de luz girando (no lugar das asas)
            for (int a = 0; a < 3; a++)
            {
                double ang0 = t * 4 + a * Math.PI * 2 / 3;
                int r = (int)(tam * 1.2f);

                for (int s = 0; s < 6; s++)
                {
                    double ang = ang0 + s * 0.13;
                    Ponto(batcher, pos.X + (int)(Math.Cos(ang) * r), pos.Y + (int)(Math.Sin(ang) * r * 0.8), 2, Color.White, 0.75f - s * 0.1f);
                }
            }

            // faiscas piscando em volta
            for (int s = 0; s < 6; s++)
            {
                double ang = t * 1.7 + s * 1.05;
                float rr = tam * (1.7f + 0.3f * (float)Math.Sin(s * 2.3 + t * 3));
                float pis = (float)Math.Abs(Math.Sin(t * 4 + s));
                Ponto(batcher, pos.X + (int)(Math.Cos(ang) * rr), pos.Y + (int)(Math.Sin(ang) * rr), 2, Color.Lerp(cor, Color.White, 0.5f), pis);
            }

            batcher.SetBlendState(null);

            return true;
        }

        // vai ate o alvo, da voltas e volta. ida/roda = fracoes do tempo. raio = raio das voltas
        private Point VaiERodeia(Point casa, float f, double t, float ida, float roda, float voltas, int raio)
        {
            Entity alvo = World.Get(_alvo);

            if (alvo == null)
            {
                return casa;
            }

            Point centro = Tela(alvo, 0, -22);

            if (f < ida)
            {
                float k = f / ida;
                k = k * k * (3 - 2 * k);
                Point inicio = new Point(centro.X + raio, centro.Y);

                return new Point(casa.X + (int)((inicio.X - casa.X) * k), casa.Y + (int)((inicio.Y - casa.Y) * k) - (int)(Math.Sin(k * Math.PI) * 25));
            }

            if (f < ida + roda)
            {
                double ang = (f - ida) / roda * voltas * Math.PI * 2;

                return new Point(centro.X + (int)(Math.Cos(ang) * raio), centro.Y + (int)(Math.Sin(ang) * raio * 0.45));
            }

            float v = (f - ida - roda) / (1f - ida - roda);
            v = v * v * (3 - 2 * v);
            Point fim = new Point(centro.X + raio, centro.Y);

            return new Point(fim.X + (int)((casa.X - fim.X) * v), fim.Y + (int)((casa.Y - fim.Y) * v) - (int)(Math.Sin(v * Math.PI) * 25));
        }

        // cura: espiral subindo em volta do dono + brilho verde
        private Point Cura(UltimaBatcher2D b, Mobile dono, Point pe, float f, double t)
        {
            Color verde = new Color(120, 255, 170);
            float ang = f * (float)Math.PI * 6;                 // 3 voltas
            int raio = (int)(34 - f * 12);
            int altura = (int)(f * 85);
            Point pos = new Point(pe.X + (int)(Math.Cos(ang) * raio), pe.Y - 12 - altura + (int)(Math.Sin(ang) * raio * 0.4));

            float sumir = f < 0.8f ? 1f : (1f - f) / 0.2f;
            Bola(b, pe.X, pe.Y - 45, 40, verde, 0.35f * sumir);          // brilho no corpo

            for (int k = 0; k < 2; k++)
            {
                int r = (int)(18 + f * 30 + k * 12);

                for (int a = 0; a < 40; a++)
                {
                    double aa = a / 40.0 * Math.PI * 2;
                    Ponto(b, pe.X + (int)(Math.Cos(aa) * r), pe.Y + (int)(Math.Sin(aa) * r * 0.35), 2, verde, (0.6f - k * 0.25f) * sumir);
                }
            }

            return pos;
        }

        // exploracao: vai ate o item, fica brilhando em cima e volta
        private Point Explora(UltimaBatcher2D b, Point casa, float f, double t)
        {
            Entity item = World.Get(_alvo);

            if (item == null)
            {
                return casa;
            }

            Point alvo = Tela(item, 0, -18);
            float ida = f < 0.25f ? f / 0.25f : (f > 0.8f ? (1f - f) / 0.2f : 1f);
            ida = ida * ida * (3 - 2 * ida);

            if (ida > 0.95f)
            {
                for (int i = 0; i < 10; i++)
                {
                    double a = i / 10.0 * Math.PI * 2 + t * 4;
                    Ponto(b, alvo.X + (int)(Math.Cos(a) * 16), alvo.Y + 14 + (int)(Math.Sin(a) * 6), 3, new Color(255, 240, 160), 0.9f);
                }
            }

            return new Point(casa.X + (int)((alvo.X - casa.X) * ida), casa.Y + (int)((alvo.Y - casa.Y) * ida) + (int)(Math.Sin(t * 6) * 3));
        }

        // ---------- especiais ----------
        private Point Especial(UltimaBatcher2D b, Mobile dono, Point casa, Point pe, float f, double t, Color cor)
        {
            Entity alvo = World.Get(_alvo);
            Point centro = alvo != null ? Tela(alvo, 0, -22) : casa;
            Point chao = alvo != null ? Tela(alvo, 0, 0) : pe;

            switch (_especial)
            {
                // 1 FAISCA TRIPLA: gira rapido no mob e solta 3 faiscas nele
                case 1:
                {
                    Point pos = VaiERodeia(casa, f, t, 0.25f, 0.5f, 3f, 26);

                    for (int g = 0; g < 3; g++)
                    {
                        float tg = 0.35f + g * 0.13f;            // hora de cada golpe
                        float k = (f - tg) / 0.1f;

                        if (k > 0f && k < 1f)
                        {
                            Linha(b, pos.X, pos.Y, centro.X, centro.Y, 2, new Color(255, 250, 200), 1f - k);
                            Bola(b, centro.X, centro.Y, (int)(14 + k * 12), new Color(255, 235, 140), 1f - k);
                        }
                    }

                    return pos;
                }

                // 2 PO DE FADA: rodeia o mob jogando po colorido; o mob dorme
                case 2:
                {
                    Point pos = VaiERodeia(casa, f, t, 0.25f, 0.55f, 2f, 28);

                    if (f > 0.2f && f < 0.9f)
                    {
                        for (int p = 0; p < 40; p++)
                        {
                            float fase = (Rnd(p) + f * 2.5f) % 1f;
                            int px = chao.X + (int)((Rnd(p + 50) - 0.5f) * 50);
                            int py = chao.Y - 50 + (int)(fase * 50);
                            Color c = Color.Lerp(new Color(255, 190, 255), new Color(120, 210, 255), Rnd(p + 90));
                            Ponto(b, px, py, 3, c, (float)Math.Sin(Math.PI * fase));
                        }
                    }

                    return pos;
                }

                // 3 RAIO ESTELAR: sobe acima do mob e um raio de luz cai nele
                case 3:
                {
                    Point acima = new Point(centro.X, centro.Y - 60);
                    float k = Math.Min(1f, f / 0.3f);
                    k = k * k * (3 - 2 * k);
                    Point pos = f < 0.85f
                        ? new Point(casa.X + (int)((acima.X - casa.X) * k), casa.Y + (int)((acima.Y - casa.Y) * k))
                        : new Point(acima.X + (int)((casa.X - acima.X) * (f - 0.85f) / 0.15f), acima.Y + (int)((casa.Y - acima.Y) * (f - 0.85f) / 0.15f));

                    float raio = (f - 0.35f) / 0.4f;

                    if (raio > 0f && raio < 1f)
                    {
                        float a = (float)Math.Sin(Math.PI * raio);
                        int topo = Math.Max(0, chao.Y - 420);

                        for (int w = 3; w >= 1; w--)
                        {
                            int lar = w * 6;
                            for (int yy = topo; yy < chao.Y; yy += 6)
                            {
                                Bola(b, chao.X, yy, lar, new Color(255, 245, 200), a * (0.25f + 0.25f * (3 - w)));
                            }
                        }

                        Bola(b, chao.X, chao.Y - 6, (int)(40 * a), new Color(255, 235, 150), a);
                    }

                    return pos;
                }

                // 4 PETALAS PROTETORAS: petalas giram em volta do dono (5s)
                case 4:
                {
                    Color rosa = new Color(255, 170, 215);
                    float sumir = f < 0.85f ? 1f : (1f - f) / 0.15f;

                    for (int p = 0; p < 12; p++)
                    {
                        double a = t * 2.2 + p * Math.PI / 6;
                        int alt = 15 + (p % 3) * 22;
                        int px = pe.X + (int)(Math.Cos(a) * 30);
                        int py = pe.Y - alt + (int)(Math.Sin(a) * 11);
                        Bola(b, px, py, 6, rosa, 0.9f * sumir);
                        Ponto(b, px, py, 2, Color.White, sumir);
                    }

                    Bola(b, pe.X, pe.Y - 45, 45, rosa, 0.25f * sumir);

                    double ang = t * 3;

                    return new Point(pe.X + (int)(Math.Cos(ang) * 40), pe.Y - 70 + (int)(Math.Sin(ang) * 12));
                }

                // 5 EXPLOSAO DE LUZ: gira no mob e explode em ondas
                default:
                {
                    Point pos = VaiERodeia(casa, f, t, 0.25f, 0.35f, 2f, 26);
                    float ex = (f - 0.55f) / 0.35f;

                    if (ex > 0f && ex < 1f)
                    {
                        float sai = 1f - (1f - ex) * (1f - ex);
                        Bola(b, centro.X, centro.Y, (int)(30 + 40 * sai), Color.White, 1f - ex);

                        for (int o = 0; o < 3; o++)
                        {
                            int r = (int)((25 + sai * 90) * (1f - o * 0.2f));
                            Color c = COR_NIVEL[(o + 1) % 5];

                            for (int a = 0; a < 60; a++)
                            {
                                double aa = a / 60.0 * Math.PI * 2;
                                Ponto(b, chao.X + (int)(Math.Cos(aa) * r), chao.Y + (int)(Math.Sin(aa) * r * 0.4), 4, Color.Lerp(cor, c, 0.5f), 1f - ex);
                            }
                        }

                        for (int s = 0; s < 12; s++)
                        {
                            double aa = s * Math.PI / 6;
                            Linha(b, centro.X, centro.Y, centro.X + (int)(Math.Cos(aa) * 60 * sai), centro.Y + (int)(Math.Sin(aa) * 35 * sai), 2, Color.White, (1f - ex) * 0.8f);
                        }
                    }

                    return pos;
                }
            }
        }

        // ---------- evolucao: sobe girando e explode na cor nova ----------
        private Point Evolucao(UltimaBatcher2D b, Point casa, float f, double t, ref Color cor, ref int tam)
        {
            Color velha = CorDoNivel(_nivelAntes);
            Color nova = CorDoNivel(_nivel);

            if (f < 0.45f)
            {
                // sobe girando, ainda na cor antiga, cada vez mais rapido
                float k = f / 0.45f;
                double ang = k * k * Math.PI * 8;
                cor = Color.Lerp(velha, Color.White, k * 0.6f);
                tam += (int)(k * 6);

                return new Point(casa.X + (int)(Math.Cos(ang) * 22 * (1 - k)), casa.Y - (int)(k * 40) + (int)(Math.Sin(ang) * 8 * (1 - k)));
            }

            // explode na cor nova
            float ex = (f - 0.45f) / 0.55f;
            float sai = 1f - (1f - ex) * (1f - ex);
            Point topo = new Point(casa.X, casa.Y - 40 + (int)(ex * 40));
            cor = nova;

            Bola(b, casa.X, casa.Y - 40, (int)(25 + sai * 70), Color.White, (1f - ex) * 0.9f);
            Bola(b, casa.X, casa.Y - 40, (int)(20 + sai * 55), nova, (1f - ex));

            // aneis subindo
            for (int o = 0; o < 3; o++)
            {
                int r = (int)(20 + sai * 60 + o * 10);
                int yy = casa.Y - 40 - (int)(ex * 30 * o);

                for (int a = 0; a < 48; a++)
                {
                    double aa = a / 48.0 * Math.PI * 2;
                    Ponto(b, casa.X + (int)(Math.Cos(aa) * r), yy + (int)(Math.Sin(aa) * r * 0.35), 3, nova, (1f - ex) * (0.9f - o * 0.2f));
                }
            }

            // faiscas caindo
            for (int s = 0; s < 30; s++)
            {
                double aa = Rnd(s) * Math.PI * 2;
                float d = 20 + Rnd(s + 40) * 70 * sai;
                Ponto(b, casa.X + (int)(Math.Cos(aa) * d), casa.Y - 40 + (int)(Math.Sin(aa) * d * 0.5) + (int)(ex * ex * 60), 3, Color.Lerp(nova, Color.White, 0.4f), 1f - ex);
            }

            return topo;
        }
    }
}
