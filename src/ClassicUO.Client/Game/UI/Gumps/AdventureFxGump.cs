// SPDX-License-Identifier: BSD-2-Clause
// ==========================================================================
// ADVENTURE: EFEITOS DE MAGIA modernos (desenhados pelo proprio cliente).
//  Tipo 1 = FURACAO de luz (curas): nasce pequeno e cresce.
//  Tipo 2 = CORPO EM CHAMAS (Flamestrike)  -> corpo no MobileView.cs + brasas douradas subindo aqui
//  Tipo 3 = ANEL DE AREA (Arch Cure / Arch Protection): anel abrindo no chao + brilhos subindo
//  Tipo 4 = ESCUDO (Protection): runas douradas girando + escudo de hexagonos de energia + corpo azul
//  Tipo 5 = ESPELHO (Magic Reflection): anel prateado girando + estrela de brilho
//  Tipo 6 = MALDICAO (Curse): corpo escuro (MobileView) + fumaca roxa subindo
//  Tipo 7 = GELO (Paralyze): so o corpo congelado (MobileView)
//  Tipo 8 = CHUVA DE METEOROS (Meteor Swarm): bolas de fogo caindo do ceu e explodindo
//  Tipo 9 = TERREMOTO (Earthquake): ondas pulsando no chao onde foi solto + tela tremendo
//
// Pacote proprio 0xBF sub 0xAD04 (18 bytes):
//   [tipo byte][alvo dword][R byte][G byte][B byte][tamanho byte][valor word][duracao word ms]
//   Sphere: SENDPACKET 0bf w18 w0AD04 <tipo> d<alvo> <r> <g> <b> <tamanho> w<valor> w<ms>
//   Se o tipo vier com +128 (ex.: 131 = tipo 3) o efeito e num PONTO DO CHAO:
//     alvo = X*65536 + Y   e   valor = Z + 128
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
    public class AdventureFxGump : Gump
    {
        private sealed class Efeito
        {
            public int Tipo;
            public uint Alvo;
            public bool NoChao;
            public int PX, PY, PZ;
            public Color Cor;
            public int Tamanho;
            public int Semente;
            public uint Inicio, Fim;
        }

        private static AdventureFxGump _instancia;
        private static int _sementes;
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

            bool noChao = (tipo & 0x80) != 0;
            tipo &= 0x7F;

            // Efeitos no proprio corpo: so marca o mobile, quem desenha e o MobileView
            if (!noChao && (tipo == 2 || tipo == 4 || tipo == 6 || tipo == 7))
            {
                Mobile m = world.Mobiles.Get(alvo);

                if (m != null)
                {
                    if (tipo == 2) m.AdvFogoAte = Time.Ticks + ms;
                    else if (tipo == 4) m.AdvEscudoAte = Time.Ticks + ms;
                    else if (tipo == 6) m.AdvMaldicaoAte = Time.Ticks + ms;
                    else m.AdvGeloAte = Time.Ticks + ms;
                }

                if (tipo == 7)
                {
                    return;   // gelo: so no corpo
                }
            }

            if (_instancia == null || _instancia.IsDisposed)
            {
                UIManager.Add(new AdventureFxGump(world));
            }

            _instancia._efeitos.Add(new Efeito
            {
                Tipo = tipo,
                Alvo = alvo,
                NoChao = noChao,
                PX = (int)(alvo >> 16),
                PY = (int)(alvo & 0xFFFF),
                PZ = valor - 128,
                Cor = new Color(r, g, b),
                Tamanho = Math.Max(1, Math.Min((int)tamanho, 4)),
                Semente = ++_sementes * 7919,
                Inicio = Time.Ticks,
                Fim = Time.Ticks + ms
            });
        }

        // Pes do alvo na tela
        private bool Tela(Efeito e, out Point pe)
        {
            pe = Point.Zero;
            int wx, wy;

            if (e.NoChao)
            {
                PlayerMobile pl = World.Player;

                if (pl == null)
                {
                    return false;
                }

                int dx = e.PX - pl.X;
                int dy = e.PY - pl.Y;

                if (Math.Abs(dx) > 40 || Math.Abs(dy) > 40)
                {
                    return false;   // ponto longe demais (ou coordenada errada): nao desenha
                }
                wx = pl.RealScreenPosition.X + (dx - dy) * 22 + 22;
                wy = pl.RealScreenPosition.Y + (dx + dy) * 22 - (e.PZ - pl.Z) * 4 + 18;
            }
            else
            {
                Mobile m = World.Mobiles.Get(e.Alvo);

                if (m == null)
                {
                    return false;
                }

                wx = (int)(m.RealScreenPosition.X + m.Offset.X + 22);
                wy = (int)(m.RealScreenPosition.Y + (m.Offset.Y - m.Offset.Z)) + 18;
            }

            Camera camera = Client.Game.Scene.Camera;
            Point p = camera.WorldToScreen(new Point(wx, wy));
            pe = new Point(p.X + camera.Bounds.X, p.Y + camera.Bounds.Y);

            return true;
        }

        private static void Ponto(UltimaBatcher2D batcher, int x, int y, int tam, Color cor, float alpha)
        {
            if (alpha <= 0.01f || tam <= 0)
            {
                return;
            }

            batcher.Draw(SolidColorTextureCache.GetTexture(cor), new Rectangle(x - tam / 2, y - tam / 2, tam, tam), ShaderHueTranslator.GetHueVector(0, false, Math.Min(alpha, 1f)));
        }

        // Linha feita de pontos (para raios, rastros, rachaduras)
        private static void Linha(UltimaBatcher2D batcher, int x1, int y1, int x2, int y2, int tam, Color cor, float alpha)
        {
            int passos = Math.Max(1, Math.Max(Math.Abs(x2 - x1), Math.Abs(y2 - y1)) / Math.Max(1, tam / 2));

            for (int i = 0; i <= passos; i++)
            {
                float f = i / (float)passos;
                Ponto(batcher, x1 + (int)((x2 - x1) * f), y1 + (int)((y2 - y1) * f), tam, cor, alpha);
            }
        }

        // Elipse no chao (perspectiva do UO: achatada)
        private static void Anel(UltimaBatcher2D batcher, int cx, int cy, int raio, float achata, int pontos, int tam, Color cor, float alpha, float giro = 0f)
        {
            for (int a = 0; a < pontos; a++)
            {
                double ang = a / (double)pontos * Math.PI * 2 + giro;
                Ponto(batcher, cx + (int)(Math.Cos(ang) * raio), cy + (int)(Math.Sin(ang) * raio * achata), tam, cor, alpha);
            }
        }

        // Numero "aleatorio" fixo por efeito (o mesmo a cada quadro, para nao piscar)
        private static float Rnd(int semente, int i)
        {
            uint h = (uint)(semente * 374761393 + i * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;

            return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
        }

        public override bool Draw(UltimaBatcher2D batcher, int x, int y)
        {
            if (_efeitos.Count == 0)
            {
                return true;
            }

            batcher.SetBlendState(BlendState.Additive);   // luz somada: cores vivas e brilhantes

            for (int i = _efeitos.Count - 1; i >= 0; i--)
            {
                Efeito e = _efeitos[i];

                if (Time.Ticks >= e.Fim)
                {
                    _efeitos.RemoveAt(i);
                    continue;
                }

                if (!Tela(e, out Point pe))
                {
                    continue;
                }

                float t = (Time.Ticks - e.Inicio) / (float)(e.Fim - e.Inicio);   // 0 -> 1
                float seg = (Time.Ticks - e.Inicio) / 1000f;                     // segundos desde o inicio
                float sumir = t < 0.75f ? 1f : 1f - (t - 0.75f) / 0.25f;         // some no final

                switch (e.Tipo)
                {
                    case 1: DesenhaFuracao(batcher, e, pe, t, sumir); break;
                    case 2: DesenhaBrasas(batcher, e, pe, t, seg, sumir); break;
                    case 3: DesenhaAnelArea(batcher, e, pe, t, seg, sumir); break;
                    case 4: DesenhaEscudo(batcher, e, pe, t, seg, sumir); break;
                    case 5: DesenhaEspelho(batcher, e, pe, t, seg, sumir); break;
                    case 6: DesenhaFumaca(batcher, e, pe, t, seg, sumir); break;
                    case 8: DesenhaMeteoros(batcher, e, pe, t, seg); break;
                    case 9: DesenhaTerremoto(batcher, e, pe, t, seg, sumir); break;
                }
            }

            batcher.SetBlendState(null);

            return true;
        }

        // ---------------- 1: FURACAO (curas) ----------------
        private static void DesenhaFuracao(UltimaBatcher2D batcher, Efeito e, Point pe, float t, float sumir)
        {
            float cresce = 0.3f + 0.7f * Math.Min(1f, t / 0.45f);           // comeca pequeno e vai crescendo
            float escala = (1.0f + 0.4f * e.Tamanho) * cresce;
            int altura = (int)(170 * escala);
            float giro = (Time.Ticks - e.Inicio) / 1000f * 7f;
            Color clara = Color.Lerp(e.Cor, Color.White, 0.55f);
            Color dourada = new Color(255, 240, 150);

            Anel(batcher, pe.X, pe.Y, (int)((25 + 70 * t) * escala), 0.35f, 48, 3, dourada, 0.8f * (1f - t));

            for (int k = 0; k < 9; k++)
            {
                float fase = (k / 9f + t * 0.9f) % 1f;
                int raio = (int)((18 + fase * 70) * escala);
                float alfa = (float)Math.Sin(Math.PI * fase) * 0.55f * sumir;
                Anel(batcher, pe.X, pe.Y - (int)(fase * altura), raio, 0.28f, 36, 2, e.Cor, alfa, giro);
            }

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
                    Ponto(batcher, px, py, Math.Sin(ang) > 0 ? 4 : 3, c, (float)Math.Sin(Math.PI * fase) * sumir);
                }
            }
        }

        // ---------------- 3: ANEL DE AREA (Arch Cure / Arch Protection) ----------------
        private static void DesenhaAnelArea(UltimaBatcher2D batcher, Efeito e, Point pe, float t, float seg, float sumir)
        {
            float escala = 0.8f + 0.3f * e.Tamanho;
            Color clara = Color.Lerp(e.Cor, Color.White, 0.5f);
            float abre = Math.Min(1f, t / 0.5f);

            // 3 aneis abrindo do centro para fora
            for (int k = 0; k < 3; k++)
            {
                float f = Math.Max(0f, abre - k * 0.15f);
                int raio = (int)((20 + 110 * f) * escala);
                Anel(batcher, pe.X, pe.Y, raio, 0.4f, 64, k == 0 ? 4 : 3, k == 0 ? clara : e.Cor, (0.9f - k * 0.25f) * sumir, seg * 1.5f);
            }

            // brilhos subindo dentro da area
            for (int p = 0; p < 40; p++)
            {
                float fase = (Rnd(e.Semente, p) + seg * 0.8f) % 1f;
                double ang = Rnd(e.Semente, p + 100) * Math.PI * 2;
                float dist = Rnd(e.Semente, p + 200) * 120 * escala * abre;
                int px = pe.X + (int)(Math.Cos(ang) * dist);
                int py = pe.Y + (int)(Math.Sin(ang) * dist * 0.4f) - (int)(fase * 90 * escala);
                Ponto(batcher, px, py, p % 4 == 0 ? 4 : 3, p % 3 == 0 ? Color.White : clara, (float)Math.Sin(Math.PI * fase) * sumir);
            }
        }

        // ---------------- 4: ESCUDO (Protection) ----------------
        // 1) 4 runas douradas entram girando e se fecham no peito
        // 2) FLASH: aparece um escudo de hexagonos de energia (favos) em volta do corpo, que pisca e se desfaz
        // 3) as runas continuam orbitando devagar ate o fim
        private static void DesenhaEscudo(UltimaBatcher2D batcher, Efeito e, Point pe, float t, float seg, float sumir)
        {
            float escala = 0.9f + 0.2f * e.Tamanho;
            Color ouro = new Color(255, 215, 90);
            Color azul = Color.Lerp(e.Cor, Color.White, 0.25f);
            Color clara = Color.Lerp(e.Cor, Color.White, 0.7f);
            int cx = pe.X;
            int cy = pe.Y - (int)(40 * escala);   // peito

            // ---- runas ----
            float fecha = Math.Min(1f, t / 0.3f);                        // 0 -> 1 nos primeiros 30%
            float raioRuna = (80f - 38f * fecha) * escala;
            float giroRuna = seg * (6f - 4f * fecha);                    // gira rapido entrando, depois devagar

            for (int r = 0; r < 4; r++)
            {
                double ang = r * Math.PI / 2 + giroRuna;
                int rx = cx + (int)(Math.Cos(ang) * raioRuna);
                int ry = cy + (int)(Math.Sin(ang) * raioRuna * 0.45f);
                int tam = (int)(7 * escala);

                // runa = losango com um traco no meio
                Linha(batcher, rx, ry - tam, rx + tam, ry, 2, ouro, sumir);
                Linha(batcher, rx + tam, ry, rx, ry + tam, 2, ouro, sumir);
                Linha(batcher, rx, ry + tam, rx - tam, ry, 2, ouro, sumir);
                Linha(batcher, rx - tam, ry, rx, ry - tam, 2, ouro, sumir);
                Linha(batcher, rx, ry - tam / 2, rx, ry + tam / 2, 2, Color.White, sumir);
                Ponto(batcher, rx, ry, 10, ouro, 0.25f * sumir);   // brilho da runa

                // rastro da runa enquanto entra
                if (fecha < 1f)
                {
                    for (int k = 1; k <= 4; k++)
                    {
                        double a2 = ang - k * 0.18;
                        Ponto(batcher, cx + (int)(Math.Cos(a2) * raioRuna), cy + (int)(Math.Sin(a2) * raioRuna * 0.45f), 4, ouro, (1f - k / 5f) * 0.6f);
                    }
                }
            }

            // ---- flash quando as runas fecham ----
            float flash = (t - 0.3f) / 0.12f;

            if (flash > 0f && flash < 1f)
            {
                Ponto(batcher, cx, cy, (int)(70 * escala * flash), clara, (1f - flash) * 0.6f);
                Anel(batcher, pe.X, pe.Y, (int)((30 + flash * 70) * escala), 0.4f, 48, 4, azul, 1f - flash);
            }

            // ---- escudo de hexagonos (favos de energia) ----
            float hexT = (t - 0.3f) / 0.7f;   // 0 -> 1 do flash ate o fim

            if (hexT > 0f)
            {
                int raio = (int)(52 * escala);
                int h = (int)(11 * escala);                              // tamanho do hexagono
                float dx = h * 1.5f, dy = h * 1.732f;
                float brilho = hexT < 0.15f ? 1f : (0.35f + 0.2f * (float)Math.Sin(seg * 7)) * sumir;

                for (int col = -5; col <= 5; col++)
                {
                    for (int lin = -5; lin <= 5; lin++)
                    {
                        float hx = col * dx;
                        float hy = lin * dy + (col % 2 != 0 ? dy / 2 : 0);
                        float d = (float)Math.Sqrt(hx * hx + hy * hy);

                        if (d > raio)
                        {
                            continue;
                        }

                        // os favos de fora brilham mais (borda do escudo) e se desfazem por ultimo
                        float borda = d / raio;
                        float some = hexT > 0.7f ? Math.Max(0f, 1f - (hexT - 0.7f) / 0.3f * (1.6f - borda)) : 1f;
                        float alfa = brilho * (0.25f + 0.75f * borda) * some;

                        if (alfa <= 0.02f)
                        {
                            continue;
                        }

                        int px = cx + (int)hx, py = cy + (int)hy;

                        for (int v = 0; v < 6; v++)
                        {
                            double a1 = v * Math.PI / 3, a2 = (v + 1) * Math.PI / 3;
                            Linha(batcher, px + (int)(Math.Cos(a1) * h * 0.92), py + (int)(Math.Sin(a1) * h * 0.92),
                                px + (int)(Math.Cos(a2) * h * 0.92), py + (int)(Math.Sin(a2) * h * 0.92), 2, borda > 0.75f ? clara : azul, alfa);
                        }

                        // particula saindo do favo quando o escudo se desfaz
                        if (hexT > 0.7f)
                        {
                            float sobe = (hexT - 0.7f) / 0.3f;
                            Ponto(batcher, px, py - (int)(sobe * 30), 3, clara, (1f - sobe) * 0.8f);
                        }
                    }
                }
            }
        }

        // ---------------- 5: ESPELHO (Magic Reflection) ----------------
        private static void DesenhaEspelho(UltimaBatcher2D batcher, Efeito e, Point pe, float t, float seg, float sumir)
        {
            float escala = 0.9f + 0.25f * e.Tamanho;
            Color prata = new Color(220, 230, 255);
            int cy = pe.Y - (int)(35 * escala);

            // dois aneis cruzados girando em volta do corpo
            for (int a = 0; a < 48; a++)
            {
                double ang = a / 48.0 * Math.PI * 2 + seg * 4;
                int r = (int)(40 * escala);
                int x1 = pe.X + (int)(Math.Cos(ang) * r);
                Ponto(batcher, x1, cy + (int)(Math.Sin(ang) * r * 0.35) - 12, 3, prata, 0.75f * sumir);
                Ponto(batcher, x1, cy + (int)(Math.Sin(ang) * r * 0.35) + 12, 2, e.Cor, 0.6f * sumir);
            }

            // estilhacos de espelho girando
            for (int p = 0; p < 8; p++)
            {
                double ang = p / 8.0 * Math.PI * 2 - seg * 3;
                int r = (int)(52 * escala);
                Ponto(batcher, pe.X + (int)(Math.Cos(ang) * r), cy + (int)(Math.Sin(ang) * r * 0.45), 5, Color.White, 0.9f * sumir);
            }

            // estrela de brilho no comeco
            if (t < 0.35f)
            {
                float f = 1f - t / 0.35f;
                int tam = (int)(60 * escala * (1f - f * 0.5f));
                Linha(batcher, pe.X - tam, cy, pe.X + tam, cy, 3, Color.White, f);
                Linha(batcher, pe.X, cy - tam, pe.X, cy + tam, 3, Color.White, f);
                Ponto(batcher, pe.X, cy, 12, Color.White, f);
            }
        }

        // ---------------- 6: FUMACA ROXA (Curse) ----------------
        private static void DesenhaFumaca(UltimaBatcher2D batcher, Efeito e, Point pe, float t, float seg, float sumir)
        {
            Color roxo = new Color(150, 40, 220);
            Color escuro = new Color(90, 10, 140);

            for (int p = 0; p < 30; p++)
            {
                float fase = (Rnd(e.Semente, p) + seg * 0.45f) % 1f;
                int px = pe.X + (int)((Rnd(e.Semente, p + 50) - 0.5f) * 40 + Math.Sin(seg * 3 + p) * 8 * fase);
                int py = pe.Y - 20 - (int)(fase * 110);
                int tam = 4 + (int)(fase * 10);   // a fumaca abre conforme sobe
                Ponto(batcher, px, py, tam, p % 2 == 0 ? roxo : escuro, (float)Math.Sin(Math.PI * fase) * 0.45f * sumir);
            }

            // simbolo no chao (anel roxo)
            Anel(batcher, pe.X, pe.Y, 34, 0.35f, 40, 3, roxo, 0.6f * sumir, -seg * 2);
        }

        // ---------------- 2: BRASAS DOURADAS (Flamestrike) ----------------
        private static void DesenhaBrasas(UltimaBatcher2D batcher, Efeito e, Point pe, float t, float seg, float sumir)
        {
            Color ouro = new Color(255, 200, 60);
            Color laranja = new Color(255, 120, 20);

            // brilho quente no chao
            Anel(batcher, pe.X, pe.Y, 30 + (int)(Math.Sin(seg * 10) * 3), 0.4f, 40, 5, laranja, 0.55f * sumir);
            Anel(batcher, pe.X, pe.Y, 20, 0.4f, 32, 4, ouro, 0.6f * sumir);

            // brasas subindo em volta do corpo, abrindo para os lados
            for (int p = 0; p < 36; p++)
            {
                float fase = (Rnd(e.Semente, p) + seg * 1.4f) % 1f;
                float lado = (Rnd(e.Semente, p + 60) - 0.5f) * 2f;   // -1 esquerda, +1 direita
                int px = pe.X + (int)(lado * (14 + fase * 34) + Math.Sin(seg * 8 + p) * 4);
                int py = pe.Y - 6 - (int)(fase * 120);
                Ponto(batcher, px, py, fase < 0.5f ? 4 : 3, p % 3 == 0 ? Color.White : (p % 3 == 1 ? ouro : laranja), (float)Math.Sin(Math.PI * fase) * sumir);
            }
        }

        // ---------------- 8: CHUVA DE METEOROS (Meteor Swarm) ----------------
        private static void DesenhaMeteoros(UltimaBatcher2D batcher, Efeito e, Point pe, float t, float seg)
        {
            float total = (e.Fim - e.Inicio) / 1000f;
            int qtd = 22 + e.Tamanho * 4;   // area grande: muitos meteoros
            Color laranja = new Color(255, 130, 20);
            Color amarelo = new Color(255, 230, 90);
            Color vermelho = new Color(255, 50, 10);

            for (int m = 0; m < qtd; m++)
            {
                float comeca = Rnd(e.Semente, m) * total * 0.72f;   // cada meteoro sai numa hora
                float queda = 0.5f;                                 // tempo caindo (s)
                float f = (seg - comeca) / queda;

                if (f < 0f)
                {
                    continue;
                }

                // onde cai: espalhado em volta do ponto
                double ang = Rnd(e.Semente, m + 40) * Math.PI * 2;
                float dist = (0.2f + 0.8f * (float)Math.Sqrt(Rnd(e.Semente, m + 80))) * 330;   // espalhados ao redor (uns 7-8 tiles)
                int ix = pe.X + (int)(Math.Cos(ang) * dist);
                int iy = pe.Y + (int)(Math.Sin(ang) * dist * 0.45f);
                int desvio = (int)((Rnd(e.Semente, m + 120) - 0.5f) * 120);   // vem levemente de lado, cada um de um jeito

                if (f < 1f)
                {
                    // caindo quase reto do ceu, com rastro de fogo
                    float q = f * f;   // acelera
                    int cx = ix + (int)((1 - q) * desvio);
                    int cy = iy - (int)((1 - q) * 380);

                    for (int r = 0; r < 12; r++)
                    {
                        Ponto(batcher, cx + (int)(r * desvio / 38f), cy - r * 10, 12 - r, r < 3 ? amarelo : (r < 7 ? laranja : vermelho), (1f - r / 12f) * 0.8f);
                    }

                    Ponto(batcher, cx, cy, 14, amarelo, 1f);
                    Ponto(batcher, cx, cy, 7, Color.White, 1f);
                }
                else
                {
                    // explosao no chao
                    float ex = (seg - comeca - queda) / 0.6f;

                    if (ex < 1f)
                    {
                        Anel(batcher, ix, iy, (int)(10 + ex * 50), 0.42f, 40, 5, ex < 0.4f ? amarelo : laranja, 1f - ex);

                        for (int s = 0; s < 12; s++)
                        {
                            double a2 = s / 12.0 * Math.PI * 2;
                            int d = (int)(ex * 40);
                            Ponto(batcher, ix + (int)(Math.Cos(a2) * d), iy - (int)(Math.Abs(Math.Sin(a2)) * d * 1.3f), 5, vermelho, 1f - ex);
                        }

                        Ponto(batcher, ix, iy - 10, (int)(36 * (1 - ex)), amarelo, 0.7f * (1 - ex));
                    }
                }
            }
        }

        // ---------------- 9: TERREMOTO (Earthquake): ondas pulsando ----------------
        private static void DesenhaTerremoto(UltimaBatcher2D batcher, Efeito e, Point pe, float t, float seg, float sumir)
        {
            Color laranja = new Color(255, 140, 40);
            Color ouro = new Color(255, 210, 110);
            Color terra = new Color(150, 100, 50);
            float escala = 0.8f + 0.3f * e.Tamanho;
            const float PULSO = 0.55f;   // uma onda a cada 0,55 s

            int ondas = (int)(seg / PULSO) + 1;

            for (int k = Math.Max(0, ondas - 4); k < ondas; k++)
            {
                float f = (seg - k * PULSO) / 1.3f;   // cada onda vive 1,3 s

                if (f < 0f || f > 1f)
                {
                    continue;
                }

                float sai = 1f - (1f - f) * (1f - f);            // abre rapido e freia (estilo moderno)
                int raio = (int)((40 + sai * 230) * escala);
                float alfa = (1f - f) * sumir;

                // a onda e uma faixa: borda brilhante + rastro escurecendo para dentro
                Anel(batcher, pe.X, pe.Y, raio, 0.42f, 120, 5, ouro, alfa);
                Anel(batcher, pe.X, pe.Y, raio - 8, 0.42f, 110, 4, laranja, alfa * 0.7f);
                Anel(batcher, pe.X, pe.Y, raio - 18, 0.42f, 100, 4, terra, alfa * 0.45f);

                // poeira e pedrinhas levantando na frente da onda
                for (int p = 0; p < 26; p++)
                {
                    double ang = p / 26.0 * Math.PI * 2 + Rnd(e.Semente, p + k * 31) * 0.3;
                    int sobe = (int)(Math.Sin(Math.PI * Math.Min(1f, f * 2f)) * (6 + Rnd(e.Semente, p + 77) * 14));
                    int px = pe.X + (int)(Math.Cos(ang) * raio);
                    int py = pe.Y + (int)(Math.Sin(ang) * raio * 0.42f) - sobe;
                    Ponto(batcher, px, py, 4, p % 2 == 0 ? terra : ouro, alfa * 0.8f);
                }
            }
        }

        // ---------------- TELA TREMENDO (chamado pelo GameScene a cada quadro) ----------------
        public static void Tremor(ref int x, ref int y)
        {
            if (_instancia == null || _instancia.IsDisposed || _instancia._efeitos.Count == 0)
            {
                return;
            }

            float forca = 0f;

            foreach (Efeito e in _instancia._efeitos)
            {
                if (e.Tipo != 9 || Time.Ticks >= e.Fim)
                {
                    continue;
                }

                float t = (Time.Ticks - e.Inicio) / (float)(e.Fim - e.Inicio);
                float seg = (Time.Ticks - e.Inicio) / 1000f;
                float pulso = (float)Math.Pow(Math.Abs(Math.Sin(seg * Math.PI / 0.55)), 3);   // treme mais forte a cada onda
                forca = Math.Max(forca, (1f - t) * (4f + 7f * pulso));   // forca do tremor (pixels)
            }

            if (forca <= 0.5f)
            {
                return;
            }

            double tt = Time.Ticks / 1000.0;
            x += (int)(Math.Sin(tt * 61) * forca);
            y += (int)(Math.Cos(tt * 47) * forca * 0.7f);
        }
    }
}
