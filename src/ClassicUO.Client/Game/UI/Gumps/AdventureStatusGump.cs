// SPDX-License-Identifier: BSD-2-Clause
// ==========================================================================
// ADVENTURE: FAIXA DE VIDA / MANA / STAMINA NO TOPO DO JOGO (opcao C da previa).
//
// - Faixa escura na largura toda da janela, com linha dourada embaixo.
// - Nome do personagem a esquerda e 3 barras: VIDA (vermelha), MANA (azul), STAM (verde),
//   com o valor "atual/maximo" escrito dentro. Brilho em cima e borda dourada, no estilo da barra de boss.
// - Nao depende de pacote do servidor: usa a vida/mana/stam que o cliente ja recebe do Sphere.
// - Arrastar com o mouse muda so a ALTURA da faixa (ela sempre ocupa a largura toda).
//   A altura fica salva em adventure_status.txt (pasta do perfil).
// - Liga/desliga em Opcoes > Interface > Avisos Adventure > "Faixa de vida/mana/stam no topo".
// ==========================================================================

using System;
using System.IO;
using ClassicUO.Assets;
using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.UI.Gumps
{
    public class AdventureStatusGump : Gump
    {
        // ---------- ajustes ----------
        private const int ALT = 26;           // altura da faixa
        private const int ALT_BARRA = 16;     // altura de cada barra
        private const int LARG_NOME = 130;    // espaco do nome a esquerda
        private const int LARG_MAX = 320;     // largura maxima de cada barra
        private const int ESPACO = 14;        // espaco entre as barras
        private const string ARQUIVO = "adventure_status.txt";

        private static readonly Color COR_FUNDO = new Color(10, 8, 6);
        private static readonly Color COR_OURO = new Color(200, 160, 70);
        private static readonly Color COR_OURO2 = new Color(110, 85, 40);
        private static readonly Color COR_TRILHO = new Color(22, 19, 16);

        // cor escura (esquerda) e clara (brilho de cima) de cada barra
        private static readonly string[] NOMES = { "VIDA", "MANA", "STAM" };
        private static readonly Color[] COR_BARRA = { new Color(185, 30, 28), new Color(40, 90, 205), new Color(45, 150, 55) };
        private static readonly Color[] COR_BRILHO = { new Color(240, 85, 70), new Color(95, 155, 245), new Color(120, 215, 100) };

        private static AdventureStatusGump _instancia;

        private readonly TextBox[] _rotulo = new TextBox[3];
        private readonly TextBox[] _valor = new TextBox[3];
        private readonly string[] _textoValor = { "", "", "" };
        private TextBox _nome;
        private string _textoNome = "";

        public AdventureStatusGump(World world) : base(world, 0, 0)
        {
            _instancia = this;
            CanMove = true;
            AcceptMouseInput = true;
            CanCloseWithRightClick = false;   // sempre na tela (desliga pelas opcoes)
            WantUpdateSize = false;
            Height = ALT;
            Width = Math.Max(400, ScaleHelper.LogicalWindowWidth);

            for (int i = 0; i < 3; i++)
            {
                _rotulo[i] = TextBox.GetOne(NOMES[i], EmbeddedFontNames.ROBOTO_BOLD, 12, Color.White, TextBox.RTLOptions.Default(60));
            }

            CarregarPosicao();
        }

        // ---------- chamados de fora ----------

        // Abre no login, se a opcao estiver ligada
        public static void AbrirSeLigada(World world)
        {
            if (ProfileManager.CurrentProfile?.AdvBarraStatus == false)
            {
                Fechar();

                return;
            }

            if (_instancia == null || _instancia.IsDisposed)
            {
                UIManager.Add(new AdventureStatusGump(world));
            }
        }

        public static void Fechar()
        {
            if (_instancia != null && !_instancia.IsDisposed)
            {
                _instancia.Dispose();
            }

            _instancia = null;
        }

        // ---------- posicao (so a altura muda; X sempre 0) ----------
        private string CaminhoArquivo()
        {
            return Path.Combine(ProfileManager.ProfilePath ?? "", ARQUIVO);
        }

        private void CarregarPosicao()
        {
            X = 0;
            Y = 0;

            try
            {
                string arq = CaminhoArquivo();

                if (File.Exists(arq) && int.TryParse(File.ReadAllText(arq).Trim(), out int y))
                {
                    Y = Math.Max(0, y);
                }
            }
            catch
            {
                // sem arquivo: fica no topo
            }
        }

        private void SalvarPosicao()
        {
            try
            {
                File.WriteAllText(CaminhoArquivo(), Y.ToString());
            }
            catch
            {
                // sem problema: volta para o topo no proximo login
            }
        }

        protected override void OnDragEnd(int x, int y)
        {
            base.OnDragEnd(x, y);
            X = 0;
            Y = Math.Max(0, Math.Min(Y, ScaleHelper.LogicalWindowHeight - ALT));
            SalvarPosicao();
        }

        // ---------- atualizacao ----------
        public override void Update()
        {
            base.Update();

            if (IsDisposed)
            {
                return;
            }

            // acompanha o tamanho da janela do jogo
            X = 0;
            Width = Math.Max(400, ScaleHelper.LogicalWindowWidth);

            PlayerMobile p = World.Player;

            if (p == null)
            {
                return;
            }

            AtualizarTexto(0, p.Hits, p.HitsMax);
            AtualizarTexto(1, p.Mana, p.ManaMax);
            AtualizarTexto(2, p.Stamina, p.StaminaMax);

            string nome = string.IsNullOrEmpty(p.Name) ? "" : p.Name;

            if (_nome == null || nome != _textoNome)
            {
                _textoNome = nome;
                _nome?.Dispose();
                _nome = TextBox.GetOne(nome, EmbeddedFontNames.ROBOTO_BOLD, 13, new Color(235, 195, 110), TextBox.RTLOptions.Default(LARG_NOME - 16));
            }
        }

        // so recria o texto quando o valor muda (economiza)
        private void AtualizarTexto(int i, int atual, int maximo)
        {
            string t = atual + "/" + maximo;

            if (_valor[i] != null && t == _textoValor[i])
            {
                return;
            }

            _textoValor[i] = t;
            _valor[i]?.Dispose();
            _valor[i] = TextBox.GetOne(t, EmbeddedFontNames.ROBOTO_BOLD, 12, Color.White, TextBox.RTLOptions.DefaultRightAligned(90));
        }

        // ---------- desenho ----------
        public override bool Draw(UltimaBatcher2D batcher, int x, int y)
        {
            if (!base.Draw(batcher, x, y))
            {
                return false;
            }

            PlayerMobile p = World.Player;

            if (p == null)
            {
                return true;
            }

            // faixa escura + linha dourada embaixo
            Ret(batcher, x, y, Width, ALT, COR_FUNDO, 0.85f);
            Ret(batcher, x, y + ALT - 1, Width, 1, COR_OURO, 0.9f);

            _nome?.Draw(batcher, x + 10, y + (ALT - (_nome?.Height ?? 0)) / 2);

            // 3 barras dividindo o espaco que sobra (no maximo LARG_MAX cada)
            int livre = Width - LARG_NOME - 10;
            int larg = Math.Max(80, Math.Min(LARG_MAX, (livre - ESPACO * 2) / 3));
            int bx = x + LARG_NOME;
            int by = y + (ALT - ALT_BARRA) / 2;

            DesenhaBarra(batcher, 0, bx, by, larg, p.Hits, p.HitsMax);
            DesenhaBarra(batcher, 1, bx + (larg + ESPACO), by, larg, p.Mana, p.ManaMax);
            DesenhaBarra(batcher, 2, bx + (larg + ESPACO) * 2, by, larg, p.Stamina, p.StaminaMax);

            return true;
        }

        private void DesenhaBarra(UltimaBatcher2D batcher, int i, int x, int y, int w, int atual, int maximo)
        {
            float pct = maximo > 0 ? Math.Max(0f, Math.Min(1f, atual / (float)maximo)) : 0f;
            int cheio = (int)((w - 2) * pct);

            Ret(batcher, x, y, w, ALT_BARRA, COR_TRILHO, 0.95f);                       // trilho escuro
            Ret(batcher, x + 1, y + 1, cheio, ALT_BARRA - 2, COR_BARRA[i]);             // valor
            Ret(batcher, x + 1, y + 1, cheio, (ALT_BARRA - 2) / 3, COR_BRILHO[i], 0.6f); // brilho em cima
            Ret(batcher, x + 1 + cheio - 2, y + 1, Math.Min(2, cheio), ALT_BARRA - 2, COR_BRILHO[i], 0.8f); // ponta clara (degrade)
            Borda(batcher, x, y, w, ALT_BARRA, COR_OURO2);

            int ty = y + (ALT_BARRA - (_rotulo[i]?.Height ?? 14)) / 2;
            _rotulo[i]?.Draw(batcher, x + 6, ty);
            _valor[i]?.Draw(batcher, x + w - 96, ty);
        }

        private static void Ret(UltimaBatcher2D batcher, int x, int y, int w, int h, Color cor, float alpha = 1f)
        {
            if (w <= 0 || h <= 0)
            {
                return;
            }

            batcher.Draw(SolidColorTextureCache.GetTexture(cor), new Rectangle(x, y, w, h), ShaderHueTranslator.GetHueVector(0, false, alpha));
        }

        private static void Borda(UltimaBatcher2D batcher, int x, int y, int w, int h, Color cor)
        {
            batcher.DrawRectangle(SolidColorTextureCache.GetTexture(cor), x, y, w, h, ShaderHueTranslator.GetHueVector(0, false, 1f));
        }

        public override void Dispose()
        {
            for (int i = 0; i < 3; i++)
            {
                _rotulo[i]?.Dispose();
                _valor[i]?.Dispose();
            }

            _nome?.Dispose();

            if (_instancia == this)
            {
                _instancia = null;
            }

            base.Dispose();
        }
    }
}
