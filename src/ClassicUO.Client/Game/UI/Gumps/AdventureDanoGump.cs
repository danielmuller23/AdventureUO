// SPDX-License-Identifier: BSD-2-Clause
// ==========================================================================
// ADVENTURE: NUMEROS DE DANO FLUTUANTES (desenhados pelo proprio cliente).
//
// Pacote proprio 0xBF sub 0xAD03 (12 bytes no total):
//   [UID do alvo dword][valor word][tipo byte]
//   Sphere: SENDPACKET 0bf w12 w0AD03 d<uid> w<valor> <tipo>
//
// Tipos (cor):
//   0 dano no mob     -> branco
//   1 dano recebido   -> vermelho
//   2 cura            -> verde, com "+"
//   3 veneno          -> verde-escuro
//   4 refletido       -> laranja
//   5 critico         -> amarelo e maior
//
// O numero nasce em cima da cabeca do mobile, sobe e some em ~1 s, com um
// desvio para os lados quando varios chegam juntos (para nao empilhar).
// Valor 0 aparece como "0". NAO vai para o journal.
// ==========================================================================

using System;
using System.Collections.Generic;
using ClassicUO.Assets;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.UI.Gumps
{
    public class AdventureDanoGump : Gump
    {
        // ---------- ajustes ----------
        private const uint DURACAO_MS = 1000;   // tempo de vida do numero
        private const int SUBIDA_PX = 46;       // quanto sobe ate sumir
        private const int TAM_NORMAL = 18;      // tamanho da fonte
        private const int TAM_CRITICO = 26;     // tamanho do critico
        private const int LARGURA = 120;        // caixa do texto (centralizado nela)
        private const int MAX_NUMEROS = 80;     // seguranca contra spam
        private const uint JANELA_PILHA_MS = 700; // numeros nesse intervalo no mesmo alvo vao para os lados

        private static readonly Color[] CORES =
        {
            new Color(255, 255, 255), // 0 dano no mob
            new Color(255, 70, 60),   // 1 dano recebido
            new Color(90, 235, 90),   // 2 cura
            new Color(40, 150, 55),   // 3 veneno
            new Color(255, 150, 40),  // 4 refletido
            new Color(255, 220, 40)   // 5 critico
        };

        // posicoes laterais usadas em sequencia quando varios numeros chegam juntos
        private static readonly int[] LADOS = { 0, -20, 20, -36, 36, -10, 10, -28, 28 };

        private sealed class Numero
        {
            public uint Alvo;
            public TextBox Texto;
            public int Lado;
            public int Tremido;
            public uint Inicio;
            public Point UltimaTela;
            public bool TemTela;
        }

        private sealed class Pilha
        {
            public int Contador;
            public uint Ultimo;
        }

        private static AdventureDanoGump _instancia;
        private readonly List<Numero> _numeros = new List<Numero>();
        private readonly Dictionary<uint, Pilha> _pilhas = new Dictionary<uint, Pilha>();
        private int _sementes;

        public AdventureDanoGump(World world) : base(world, 0, 0)
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

        public static void Receber(World world, uint alvo, int valor, int tipo)
        {
            if (tipo < 0 || tipo >= CORES.Length)
            {
                tipo = 0;
            }

            Mobile m = world.Mobiles.Get(alvo);

            if (m == null)
            {
                return;   // alvo fora da tela / inexistente: nao mostra
            }

            if (_instancia == null || _instancia.IsDisposed)
            {
                UIManager.Add(new AdventureDanoGump(world));
            }

            _instancia.Adicionar(alvo, valor, tipo);
        }

        private void Adicionar(uint alvo, int valor, int tipo)
        {
            // texto: cura com "+", o resto so o numero (0 aparece como "0")
            string txt = tipo == 2 ? "+" + valor : valor.ToString();
            int tam = tipo == 5 ? TAM_CRITICO : TAM_NORMAL;

            TextBox tb = TextBox.GetOne(txt, EmbeddedFontNames.ROBOTO_BOLD, tam, CORES[tipo], TextBox.RTLOptions.DefaultCenterStroked(LARGURA));

            // varios numeros juntos no mesmo alvo: cada um vai para um lado
            if (!_pilhas.TryGetValue(alvo, out Pilha pilha))
            {
                pilha = new Pilha();
                _pilhas[alvo] = pilha;
            }

            if (Time.Ticks - pilha.Ultimo > JANELA_PILHA_MS)
            {
                pilha.Contador = 0;
            }

            int lado = LADOS[pilha.Contador % LADOS.Length];
            pilha.Contador++;
            pilha.Ultimo = Time.Ticks;

            _numeros.Add(new Numero
            {
                Alvo = alvo,
                Texto = tb,
                Lado = lado,
                Tremido = (int)(Rnd(++_sementes) * 9) - 4,   // -4..+4 px para nao ficar tudo alinhado
                Inicio = Time.Ticks
            });

            if (_numeros.Count > MAX_NUMEROS)
            {
                _numeros[0].Texto?.Dispose();
                _numeros.RemoveAt(0);
            }
        }

        private static float Rnd(int i)
        {
            uint h = (uint)(i * 668265263 + 374761393);
            h = (h ^ (h >> 13)) * 1274126177u;

            return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
        }

        // Ponto em cima da cabeca do mobile, ja em coordenadas da tela
        private bool CimaDaCabeca(uint serial, out Point tela)
        {
            tela = Point.Zero;
            Mobile m = World.Mobiles.Get(serial);

            if (m == null || m.IsDestroyed)
            {
                return false;
            }

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
            int wy = (int)(m.RealScreenPosition.Y + (m.Offset.Y - m.Offset.Z) - (height + centerY + 8));

            Camera camera = Client.Game.Scene.Camera;
            Point p = camera.WorldToScreen(new Point(wx, wy));
            tela = new Point(p.X + camera.Bounds.X, p.Y + camera.Bounds.Y);

            return true;
        }

        public override bool Draw(UltimaBatcher2D batcher, int x, int y)
        {
            if (_numeros.Count == 0)
            {
                if (_pilhas.Count > 0)
                {
                    _pilhas.Clear();   // nada na tela: esquece os contadores de lado
                }

                return true;
            }

            for (int i = _numeros.Count - 1; i >= 0; i--)
            {
                Numero n = _numeros[i];
                uint vida = Time.Ticks - n.Inicio;

                if (vida >= DURACAO_MS || n.Texto == null || n.Texto.IsDisposed)
                {
                    n.Texto?.Dispose();
                    _numeros.RemoveAt(i);
                    continue;
                }

                // segue o mobile; se ele sumiu (morreu), fica onde estava
                if (CimaDaCabeca(n.Alvo, out Point cabeca))
                {
                    n.UltimaTela = cabeca;
                    n.TemTela = true;
                }
                else if (!n.TemTela)
                {
                    continue;
                }

                float t = vida / (float)DURACAO_MS;              // 0 -> 1
                float sobe = 1f - (1f - t) * (1f - t);           // sobe rapido e desacelera
                float alpha = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f; // some nos ultimos 40%

                int px = n.UltimaTela.X + n.Lado + n.Tremido + (int)(n.Lado * 0.5f * t); // abre um pouco para o lado
                int py = n.UltimaTela.Y - (int)(SUBIDA_PX * sobe);

                n.Texto.Alpha = Math.Max(0f, Math.Min(1f, alpha));
                n.Texto.Draw(batcher, px - n.Texto.Width / 2, py - n.Texto.Height);
            }

            return true;
        }

        public override void Dispose()
        {
            foreach (Numero n in _numeros)
            {
                n.Texto?.Dispose();
            }

            _numeros.Clear();
            _pilhas.Clear();

            if (_instancia == this)
            {
                _instancia = null;
            }

            base.Dispose();
        }
    }
}
