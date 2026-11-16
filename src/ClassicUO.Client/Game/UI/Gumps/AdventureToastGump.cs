// SPDX-License-Identifier: BSD-2-Clause
// ==========================================================================
// ADVENTURE: AVISOS ESTILO TOAST (cards no canto da tela).
//
// O servidor manda uma mensagem de sistema normal com a COR 0x0AD6, no formato:
//     tipo|titulo|texto
//   Sphere: SRC.SYSMESSAGE @0AD6 2|Boss|O Lord Mummy despertou no Cemiterio de Yew!
// Essa mensagem NAO vai para o journal: vira um card.
//   Se nao tiver os dois "|", vira tipo 6 com o texto inteiro.
//
// Tipos (cor / icone):
//   1 Evento  dourado  E      4 Level             verde   +
//   2 Boss    vermelho !      5 Fada              azul    ✦ (estrela desenhada)
//   3 Trofeu  roxo     T      6 Staff             branco  i
//
// - Entra deslizando, fica 6 s e some. No maximo 3 na tela (o 4o derruba o mais antigo).
// - Clique no card fecha.
// - Opcoes (Interface > Avisos Adventure): canto da tela, modo editar (arrastar),
//   liga/desliga por tipo. Tipo desligado vai para o journal como mensagem normal.
// ==========================================================================

using System;
using System.Collections.Generic;
using ClassicUO.Assets;
using ClassicUO.Configuration;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Input;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ClassicUO.Game.UI.Gumps
{
    public class AdventureToastGump : Gump
    {
        public const ushort COR_TOAST = 0x0AD6;   // cor da mensagem que vira toast

        // ---------- ajustes ----------
        private const int LARG = 300;          // largura do card
        private const int ALT_MIN = 58;        // altura minima do card
        private const int ESPACO = 6;          // espaco entre cards
        private const int MARGEM = 14;         // distancia da borda da tela
        private const uint VIDA_MS = 6000;     // tempo na tela
        private const uint ENTRA_MS = 260;     // tempo deslizando para entrar
        private const uint SAI_MS = 400;       // tempo sumindo no final
        private const int MAX_CARDS = 3;

        private static readonly Color[] COR_TIPO =
        {
            new Color(235, 190, 60),   // 1 evento: dourado
            new Color(225, 55, 45),    // 2 boss: vermelho
            new Color(170, 95, 235),   // 3 trofeu: roxo
            new Color(70, 205, 90),    // 4 level: verde
            new Color(70, 150, 245),   // 5 leilao/encomenda: azul
            new Color(235, 235, 235)   // 6 staff: branco
        };

        private static readonly string[] ICONE = { "E", "!", "T", "+", "*", "i" };   // tipo 5 (fada): no lugar do "*" e desenhada a estrela ✦

        private sealed class Card
        {
            public int Tipo;           // 1..6
            public TextBox Titulo, Texto, Letra;
            public int Altura;
            public uint Inicio;
            public bool Exemplo;       // card do modo editar (nao some sozinho)
            public Rectangle Area;     // posicao dentro do gump (para o clique)
        }

        private static AdventureToastGump _instancia;
        private static readonly Dictionary<uint, Texture2D> _circulos = new Dictionary<uint, Texture2D>();
        private readonly List<Card> _cards = new List<Card>();   // do mais antigo para o mais novo
        private bool _editando, _arrastou;

        public AdventureToastGump(World world) : base(world, 0, 0)
        {
            _instancia = this;
            CanMove = false;
            AcceptMouseInput = true;
            CanCloseWithRightClick = false;
            WantUpdateSize = false;
            Width = LARG;
            Height = ALT_MIN;
        }

        private static AdventureToastGump Garantir(World world)
        {
            if (_instancia == null || _instancia.IsDisposed)
            {
                UIManager.Add(new AdventureToastGump(world));
            }

            return _instancia;
        }

        // ---------- mensagem do servidor ----------
        // Retorna true se virou toast (nao vai para o journal).
        // Se o tipo estiver desligado nas opcoes, devolve false e troca o texto por "Titulo: texto" para o journal.
        public static bool Receber(World world, ref string mensagem)
        {
            Separar(mensagem, out int tipo, out string titulo, out string texto);

            if (!TipoLigado(tipo))
            {
                mensagem = string.IsNullOrEmpty(titulo) ? texto : titulo + ": " + texto;

                return false;
            }

            Garantir(world).Adicionar(tipo, titulo, texto, false);

            return true;
        }

        private static void Separar(string msg, out int tipo, out string titulo, out string texto)
        {
            string[] p = msg.Split('|', 3);

            if (p.Length < 3)
            {
                tipo = 6;
                titulo = "Aviso";
                texto = msg.Trim();

                return;
            }

            if (!int.TryParse(p[0].Trim(), out tipo) || tipo < 1 || tipo > 6)
            {
                tipo = 6;
            }

            titulo = p[1].Trim();
            texto = p[2].Trim();
        }

        private static bool TipoLigado(int tipo)
        {
            Profile pr = ProfileManager.CurrentProfile;

            if (pr == null)
            {
                return true;
            }

            switch (tipo)
            {
                case 1: return pr.AdvToastEvento;
                case 2: return pr.AdvToastBoss;
                case 3: return pr.AdvToastTrofeu;
                case 4: return pr.AdvToastLevel;
                case 5: return pr.AdvToastLeilao;
                default: return pr.AdvToastStaff;
            }
        }

        // ---------- chamados pelas opcoes ----------
        public static void Teste(World world)
        {
            AdventureToastGump g = Garantir(world);
            int n = (int)(Time.Ticks / 1000 % 6) + 1;   // varia o tipo a cada clique
            string[] titulos = { "Evento", "Boss", "Trofeu", "Level", "Fada", "Staff" };
            g.Adicionar(n, titulos[n - 1], "Este e um aviso de teste do tipo " + n + ".", false);
        }

        public static void ModoEditar(World world)
        {
            AdventureToastGump g = Garantir(world);

            if (g._editando)
            {
                return;
            }

            // a posicao atual vira o ponto de partida do modo personalizado
            Profile pr = ProfileManager.CurrentProfile;
            g.Reposicionar();

            if (pr != null && pr.AdvToastCanto != 4)
            {
                pr.AdvToastX = g.X;
                pr.AdvToastY = g.Y;
                pr.AdvToastCanto = 4;
            }

            g._editando = true;
            g.CanMove = true;

            g.Adicionar(1, "Modo editar", "Arraste este aviso para onde quiser. Clique nele (sem arrastar) para terminar.", true);
        }

        public static void Reposicionar(World world)
        {
            if (_instancia != null && !_instancia.IsDisposed)
            {
                _instancia.Reposicionar();
            }
        }

        // ---------- cards ----------
        private void Adicionar(int tipo, string titulo, string texto, bool exemplo)
        {
            Color cor = COR_TIPO[tipo - 1];
            var c = new Card
            {
                Tipo = tipo,
                Inicio = Time.Ticks,
                Exemplo = exemplo,
                Titulo = TextBox.GetOne(string.IsNullOrEmpty(titulo) ? "Aviso" : titulo, EmbeddedFontNames.ROBOTO_BOLD, 14, cor, TextBox.RTLOptions.Default(LARG - 66)),
                Texto = TextBox.GetOne(texto ?? "", EmbeddedFontNames.ROBOTO, 13, Color.White, TextBox.RTLOptions.Default(LARG - 66)),
                Letra = TextBox.GetOne(ICONE[tipo - 1], EmbeddedFontNames.ROBOTO_BOLD, 15, new Color(20, 20, 24), TextBox.RTLOptions.DefaultCentered(30))
            };

            c.Altura = Math.Max(ALT_MIN, 10 + c.Titulo.Height + 2 + c.Texto.Height + 12);

            // no maximo 3: o mais antigo (que nao seja o exemplo) sai
            while (_cards.Count >= MAX_CARDS)
            {
                int velho = _cards.FindIndex(o => !o.Exemplo);

                if (velho < 0)
                {
                    break;
                }

                Liberar(_cards[velho]);
                _cards.RemoveAt(velho);
            }

            _cards.Add(c);
            Reposicionar();
        }

        private static void Liberar(Card c)
        {
            c.Titulo?.Dispose();
            c.Texto?.Dispose();
            c.Letra?.Dispose();
        }

        private bool CantoDeBaixo()
        {
            int canto = ProfileManager.CurrentProfile?.AdvToastCanto ?? 0;

            return canto == 2 || canto == 3;
        }

        private bool CantoDaEsquerda()
        {
            int canto = ProfileManager.CurrentProfile?.AdvToastCanto ?? 0;

            return canto == 1 || canto == 3 || (canto == 4 && X < ScaleHelper.LogicalWindowWidth / 2);
        }

        // Calcula o tamanho do bloco e a posicao na tela
        private void Reposicionar()
        {
            int total = 0;

            foreach (Card c in _cards)
            {
                total += c.Altura + ESPACO;
            }

            Width = LARG;
            Height = Math.Max(ALT_MIN, total - ESPACO);

            if (_editando)
            {
                return;   // no modo editar a posicao e a do mouse
            }

            Profile pr = ProfileManager.CurrentProfile;
            int canto = pr?.AdvToastCanto ?? 0;
            int tw = ScaleHelper.LogicalWindowWidth, th = ScaleHelper.LogicalWindowHeight;

            switch (canto)
            {
                case 1: X = MARGEM; Y = MARGEM; break;                                   // superior esquerdo
                case 2: X = tw - LARG - MARGEM; Y = th - Height - MARGEM; break;         // inferior direito
                case 3: X = MARGEM; Y = th - Height - MARGEM; break;                     // inferior esquerdo
                case 4: X = pr.AdvToastX; Y = pr.AdvToastY; break;                       // personalizado (arrastado)
                default: X = tw - LARG - MARGEM; Y = MARGEM + 40; break;                 // superior direito
            }

            // nunca sai da tela
            X = Math.Max(0, Math.Min(X, tw - LARG));
            Y = Math.Max(0, Math.Min(Y, th - Height));
        }

        public override void Update()
        {
            base.Update();

            if (IsDisposed)
            {
                return;
            }

            bool mudou = false;

            for (int i = _cards.Count - 1; i >= 0; i--)
            {
                Card c = _cards[i];

                if (!c.Exemplo && Time.Ticks - c.Inicio >= VIDA_MS)
                {
                    Liberar(c);
                    _cards.RemoveAt(i);
                    mudou = true;
                }
            }

            if (mudou)
            {
                Reposicionar();
            }
            else if (!_editando)
            {
                Reposicionar();   // acompanha a janela se ela mudar de tamanho
            }

            if (_cards.Count == 0 && !_editando)
            {
                Dispose();
            }
        }

        // ---------- desenho ----------
        private static void Ret(UltimaBatcher2D batcher, int x, int y, int w, int h, Color cor, float alpha)
        {
            if (w <= 0 || h <= 0 || alpha <= 0.01f)
            {
                return;
            }

            batcher.Draw(SolidColorTextureCache.GetTexture(cor), new Rectangle(x, y, w, h), ShaderHueTranslator.GetHueVector(0, false, alpha));
        }

        // estrela de 4 pontas (✦), desenhada linha a linha: larga no meio, pontas finas em cima e embaixo
        private static void Estrela(UltimaBatcher2D batcher, int cx, int cy, int raio, Color cor, float alpha)
        {
            for (int dy = -raio; dy <= raio; dy++)
            {
                float t = 1f - Math.Abs(dy) / (float)raio;          // 1 no meio, 0 nas pontas
                int meia = (int)Math.Round(raio * t * t * t);       // curva para dentro (formato ✦)
                Ret(batcher, cx - meia, cy + dy, meia * 2 + 1, 1, cor, alpha);
            }
        }

        // circulo cheio com borda suave, na cor pedida
        private static void Circulo(UltimaBatcher2D batcher, int cx, int cy, int raio, Color cor, float alpha)
        {
            if (!_circulos.TryGetValue(cor.PackedValue, out Texture2D tex))
            {
                const int N = 64;
                var dados = new Color[N * N];

                for (int yy = 0; yy < N; yy++)
                {
                    for (int xx = 0; xx < N; xx++)
                    {
                        float dx = xx - N / 2 + 0.5f, dy = yy - N / 2 + 0.5f;
                        float d = (float)Math.Sqrt(dx * dx + dy * dy);
                        float a = Math.Max(0f, Math.Min(1f, N / 2f - d));   // 1 px de borda suave
                        dados[yy * N + xx] = new Color((byte)(cor.R * a), (byte)(cor.G * a), (byte)(cor.B * a), (byte)(255 * a));
                    }
                }

                tex = new Texture2D(Client.Game.GraphicsDevice, N, N, false, SurfaceFormat.Color);
                tex.SetData(dados);
                _circulos[cor.PackedValue] = tex;
            }

            batcher.Draw(tex, new Rectangle(cx - raio, cy - raio, raio * 2, raio * 2), ShaderHueTranslator.GetHueVector(0, false, alpha));
        }

        public override bool Draw(UltimaBatcher2D batcher, int x, int y)
        {
            if (!base.Draw(batcher, x, y))
            {
                return false;
            }

            // cantos de baixo: o mais novo fica embaixo (perto do canto); de cima: o mais novo em cima
            bool deBaixo = CantoDeBaixo();
            bool daEsquerda = CantoDaEsquerda();
            int cy = 0;

            for (int k = 0; k < _cards.Count; k++)
            {
                Card c = deBaixo ? _cards[k] : _cards[_cards.Count - 1 - k];
                c.Area = new Rectangle(0, cy, LARG, c.Altura);
                DesenhaCard(batcher, c, x, y + cy, daEsquerda);
                cy += c.Altura + ESPACO;
            }

            return true;
        }

        private void DesenhaCard(UltimaBatcher2D batcher, Card c, int x, int y, bool daEsquerda)
        {
            uint vida = Time.Ticks - c.Inicio;
            Color cor = COR_TIPO[c.Tipo - 1];

            // entra deslizando (de fora da tela para dentro) e some no final
            float entra = Math.Min(1f, vida / (float)ENTRA_MS);
            entra = 1f - (1f - entra) * (1f - entra) * (1f - entra);
            int desliza = (int)((1f - entra) * (LARG + MARGEM)) * (daEsquerda ? -1 : 1);
            float alpha = entra;

            if (!c.Exemplo && vida > VIDA_MS - SAI_MS)
            {
                alpha = Math.Max(0f, (VIDA_MS - vida) / (float)SAI_MS);
            }

            x += desliza;

            // card escuro semitransparente + faixa lateral na cor do tipo
            Ret(batcher, x, y, LARG, c.Altura, new Color(16, 16, 20), 0.86f * alpha);
            Ret(batcher, x, y, 4, c.Altura, cor, alpha);
            batcher.DrawRectangle(SolidColorTextureCache.GetTexture(new Color(60, 60, 70)), x, y, LARG, c.Altura, ShaderHueTranslator.GetHueVector(0, false, 0.8f * alpha));

            // icone redondo
            int ix = x + 26, iy = y + 10 + 15;
            Circulo(batcher, ix, iy, 15, cor, alpha);
            if (c.Tipo == 5)
            {
                Estrela(batcher, ix, iy, 10, new Color(20, 20, 24), alpha);   // ✦ da fada
            }
            else
            {
                c.Letra.Alpha = alpha;
                c.Letra.Draw(batcher, ix - 15, iy - c.Letra.Height / 2);
            }

            // titulo colorido e texto branco
            c.Titulo.Alpha = alpha;
            c.Titulo.Draw(batcher, x + 52, y + 8);
            c.Texto.Alpha = alpha;
            c.Texto.Draw(batcher, x + 52, y + 10 + c.Titulo.Height);

            // linha de tempo embaixo (esvaziando)
            float resta = c.Exemplo ? 1f : Math.Max(0f, 1f - vida / (float)VIDA_MS);
            Ret(batcher, x + 4, y + c.Altura - 3, LARG - 4, 3, new Color(40, 40, 46), alpha);
            Ret(batcher, x + 4, y + c.Altura - 3, (int)((LARG - 4) * resta), 3, cor, 0.85f * alpha);
        }

        // ---------- mouse ----------
        public override void OnMouseDown(int x, int y, MouseButtonType button)
        {
            _arrastou = false;
            base.OnMouseDown(x, y, button);
        }

        protected override void OnDragBegin(int x, int y)
        {
            _arrastou = true;
            base.OnDragBegin(x, y);
        }

        protected override void OnDragEnd(int x, int y)
        {
            base.OnDragEnd(x, y);

            Profile pr = ProfileManager.CurrentProfile;

            if (_editando && pr != null)
            {
                pr.AdvToastX = X;   // fica salvo nas configuracoes (volta no relogin)
                pr.AdvToastY = Y;
                pr.AdvToastCanto = 4;
            }
        }

        public override void OnMouseUp(int x, int y, MouseButtonType button)
        {
            if (button == MouseButtonType.Left && !_arrastou)
            {
                Card alvo = _cards.Find(o => o.Area.Contains(x, y));

                if (alvo != null)
                {
                    if (alvo.Exemplo)
                    {
                        _editando = false;   // termina o modo editar
                        CanMove = false;
                    }

                    Liberar(alvo);
                    _cards.Remove(alvo);
                    Reposicionar();

                    return;
                }
            }

            _arrastou = false;
            base.OnMouseUp(x, y, button);
        }

        public override void Dispose()
        {
            foreach (Card c in _cards)
            {
                Liberar(c);
            }

            _cards.Clear();

            if (_instancia == this)
            {
                _instancia = null;
            }

            base.Dispose();
        }
    }
}
