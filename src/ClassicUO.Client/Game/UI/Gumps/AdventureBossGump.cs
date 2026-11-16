// SPDX-License-Identifier: BSD-2-Clause
// ==========================================================================
// ADVENTURE: BARRA DE VIDA DE BOSS (desenhada pelo proprio cliente).
//
// Pacote proprio 0xBF sub 0xAD06 (13 bytes no total):
//   [UID do boss dword][vida em milesimos word 0-1000][nivel byte][estado byte]
//   Sphere: SENDPACKET 0bf w13 w0AD06 d<uid> w<vida 0-1000> <nivel> <estado>
//
//   nivel  0 = "Nivel de Evento", senao "Nivel N"
//   estado 1 = apanhou (abre / atualiza)
//          2 = morreu  (mostra "DERROTADO!" e fecha em 3 s)
//          3 = player chegou perto (abre)
//
// - Varios bosses ao mesmo tempo: as barras empilham uma embaixo da outra.
// - A janela e movel; a posicao fica salva em adventure_boss.txt (pasta do perfil).
// - Botao X: fecha a barra e ignora aquele boss ate ele morrer.
// - Fecha sozinha apos 15 s sem pacote ou se o boss sair da tela.
// - Faixa amarela: o dano recente aparece em amarelo e vai sumindo atras da vida.
// ==========================================================================

using System;
using System.Collections.Generic;
using System.IO;
using ClassicUO.Assets;
using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Input;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.UI.Gumps
{
    public class AdventureBossGump : Gump
    {
        // ---------- ajustes ----------
        private const int LARG = 340;             // largura de cada barra
        private const int ALT = 62;               // altura de cada barra
        private const int ESPACO = 4;             // espaco entre barras empilhadas
        private const int X_TAM = 16;             // tamanho do botao X
        private const uint SEM_PACOTE_MS = 15000; // fecha apos 15 s sem pacote
        private const uint MORTO_MS = 3000;       // "DERROTADO!" fica 3 s
        private const uint ESPERA_AMARELO_MS = 450; // a faixa amarela espera um pouco antes de descer
        private const float AMARELO_DESCE = 700f;  // milesimos por segundo que a faixa amarela desce

        private static readonly Color COR_FUNDO = new Color(14, 12, 10);
        private static readonly Color COR_BORDA = new Color(200, 160, 70);
        private static readonly Color COR_BORDA2 = new Color(90, 70, 35);
        private static readonly Color COR_TRILHO = new Color(45, 12, 12);
        private static readonly Color COR_VIDA = new Color(190, 28, 28);
        private static readonly Color COR_VIDA_BRILHO = new Color(235, 75, 60);
        private static readonly Color COR_AMARELO = new Color(235, 195, 45);
        private static readonly Color COR_DIVISAO = new Color(15, 10, 8);
        private static readonly Color COR_MORTO = new Color(70, 70, 70);

        private sealed class Barra
        {
            public uint Uid;
            public int Vida;          // 0..1000
            public float Amarelo;     // 0..1000 (dano recente)
            public uint AmareloEspera;
            public int Nivel;
            public uint UltimoPacote;
            public bool Morreu;
            public uint MorreuEm;
            public string Nome;
            public int UltimoPct = -1;
            public TextBox TxtNome, TxtNivel, TxtPct, TxtMorto, TxtX;
        }

        private static AdventureBossGump _instancia;
        private static readonly HashSet<uint> _ignorados = new HashSet<uint>();   // fechados no X ate morrer
        private readonly List<Barra> _barras = new List<Barra>();
        private uint _ultimoUpdate;

        public AdventureBossGump(World world) : base(world, 0, 0)
        {
            _instancia = this;
            CanMove = true;
            AcceptMouseInput = true;
            CanCloseWithRightClick = false;
            WantUpdateSize = false;
            Width = LARG;
            Height = ALT;

            CarregarPosicao();
            _ultimoUpdate = Time.Ticks;
        }

        // ---------- pacote ----------
        public static void Receber(World world, uint uid, int vida, int nivel, int estado)
        {
            vida = Math.Max(0, Math.Min(1000, vida));

            if (estado == 2)
            {
                _ignorados.Remove(uid);   // morreu: o X deixa de valer
                _instancia?.Morte(uid);

                return;
            }

            if (estado != 1 && estado != 3)
            {
                return;
            }

            if (_ignorados.Contains(uid))
            {
                return;   // player fechou no X
            }

            Mobile m = world.Mobiles.Get(uid);

            if (m == null || m.IsDestroyed)
            {
                return;   // boss fora da tela
            }

            if (_instancia == null || _instancia.IsDisposed)
            {
                UIManager.Add(new AdventureBossGump(world));
            }

            _instancia.Atualizar(m, vida, nivel);
        }

        private void Atualizar(Mobile m, int vida, int nivel)
        {
            Barra b = _barras.Find(o => o.Uid == m.Serial);

            if (b == null)
            {
                b = new Barra { Uid = m.Serial, Vida = vida, Amarelo = vida };
                _barras.Add(b);
            }
            else if (b.Morreu)
            {
                return;
            }

            if (vida < b.Vida)
            {
                // apanhou: a faixa amarela fica onde estava e desce depois
                b.Amarelo = Math.Max(b.Amarelo, b.Vida);
                b.AmareloEspera = Time.Ticks + ESPERA_AMARELO_MS;
            }
            else if (vida > b.Amarelo)
            {
                b.Amarelo = vida;   // curou: sem faixa amarela
            }

            b.Vida = vida;
            b.UltimoPacote = Time.Ticks;

            string nome = string.IsNullOrEmpty(m.Name) ? "Boss" : m.Name;

            if (b.TxtNome == null || b.Nome != nome)
            {
                b.Nome = nome;
                b.TxtNome?.Dispose();
                b.TxtNome = TextBox.GetOne(nome, EmbeddedFontNames.ROBOTO_BOLD, 15, new Color(245, 225, 175), TextBox.RTLOptions.Default(LARG - 150));
            }

            if (b.TxtNivel == null || b.Nivel != nivel)
            {
                b.Nivel = nivel;
                b.TxtNivel?.Dispose();
                b.TxtNivel = TextBox.GetOne(nivel == 0 ? "Nivel de Evento" : "Nivel " + nivel, EmbeddedFontNames.ROBOTO, 12, new Color(215, 190, 130), TextBox.RTLOptions.DefaultRightAligned(130));
            }

            AjustarTamanho();
        }

        private void Morte(uint uid)
        {
            Barra b = _barras.Find(o => o.Uid == uid);

            if (b == null || b.Morreu)
            {
                return;
            }

            b.Morreu = true;
            b.MorreuEm = Time.Ticks;
            b.Vida = 0;
            b.Amarelo = 0;
            b.TxtMorto ??= TextBox.GetOne("DERROTADO!", EmbeddedFontNames.ROBOTO_BOLD, 18, new Color(255, 205, 60), TextBox.RTLOptions.DefaultCenterStroked(LARG - 20));
        }

        private void Remover(Barra b)
        {
            b.TxtNome?.Dispose();
            b.TxtNivel?.Dispose();
            b.TxtPct?.Dispose();
            b.TxtMorto?.Dispose();
            b.TxtX?.Dispose();
            _barras.Remove(b);
            AjustarTamanho();
        }

        private void AjustarTamanho()
        {
            Width = LARG;
            Height = Math.Max(ALT, _barras.Count * (ALT + ESPACO) - ESPACO);
        }

        // ---------- tempo: fechar sozinha, faixa amarela ----------
        public override void Update()
        {
            base.Update();

            if (IsDisposed)
            {
                return;
            }

            float dt = Math.Min(0.25f, (Time.Ticks - _ultimoUpdate) / 1000f);
            _ultimoUpdate = Time.Ticks;

            for (int i = _barras.Count - 1; i >= 0; i--)
            {
                Barra b = _barras[i];

                if (b.Morreu)
                {
                    if (Time.Ticks - b.MorreuEm >= MORTO_MS)
                    {
                        Remover(b);
                    }

                    continue;
                }

                Mobile m = World.Mobiles.Get(b.Uid);

                if (Time.Ticks - b.UltimoPacote > SEM_PACOTE_MS || m == null || m.IsDestroyed || m.Distance > World.ClientViewRange)
                {
                    Remover(b);   // 15 s sem pacote ou boss saiu da tela
                    continue;
                }

                if (b.Amarelo > b.Vida && Time.Ticks >= b.AmareloEspera)
                {
                    b.Amarelo = Math.Max(b.Vida, b.Amarelo - AMARELO_DESCE * dt);
                }
            }

            if (_barras.Count == 0)
            {
                Dispose();   // nada para mostrar (a posicao fica salva)
            }
        }

        // ---------- desenho ----------
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

        public override bool Draw(UltimaBatcher2D batcher, int x, int y)
        {
            if (!base.Draw(batcher, x, y))
            {
                return false;
            }

            for (int i = 0; i < _barras.Count; i++)
            {
                DesenhaBarra(batcher, _barras[i], x, y + i * (ALT + ESPACO));
            }

            return true;
        }

        private void DesenhaBarra(UltimaBatcher2D batcher, Barra b, int x, int y)
        {
            // fundo escuro + borda dourada dupla
            Ret(batcher, x, y, LARG, ALT, COR_FUNDO, 0.88f);
            Borda(batcher, x, y, LARG, ALT, COR_BORDA);
            Borda(batcher, x + 2, y + 2, LARG - 4, ALT - 4, COR_BORDA2);

            // nome e nivel
            b.TxtNome?.Draw(batcher, x + 10, y + 6);
            b.TxtNivel?.Draw(batcher, x + LARG - 14 - X_TAM - 130, y + 9);

            // botao X
            int bx = x + LARG - 8 - X_TAM, by = y + 7;
            bool sobreX = UIManager.MouseOverControl == this && Mouse.Position.X >= bx && Mouse.Position.X < bx + X_TAM && Mouse.Position.Y >= by && Mouse.Position.Y < by + X_TAM;
            Ret(batcher, bx, by, X_TAM, X_TAM, sobreX ? new Color(150, 30, 25) : new Color(45, 35, 25), 0.95f);
            Borda(batcher, bx, by, X_TAM, X_TAM, COR_BORDA2);
            b.TxtX ??= TextBox.GetOne("x", EmbeddedFontNames.ROBOTO_BOLD, 12, new Color(230, 210, 170), TextBox.RTLOptions.DefaultCentered(X_TAM));
            b.TxtX.Draw(batcher, bx, by);

            // barra
            int rx = x + 10, ry = y + 32, rw = LARG - 20, rh = 20;
            Ret(batcher, rx, ry, rw, rh, b.Morreu ? new Color(30, 30, 30) : COR_TRILHO);

            if (!b.Morreu)
            {
                int wAmarelo = (int)(rw * b.Amarelo / 1000f);
                int wVida = (int)(rw * b.Vida / 1000f);

                Ret(batcher, rx, ry, wAmarelo, rh, COR_AMARELO);            // dano recente
                Ret(batcher, rx, ry, wVida, rh, COR_VIDA);                  // vida
                Ret(batcher, rx, ry, wVida, rh / 3, COR_VIDA_BRILHO, 0.55f); // brilho em cima
            }
            else
            {
                Ret(batcher, rx, ry, rw, rh, COR_MORTO, 0.5f);
            }

            // divisoes em 1/3 e 2/3
            Ret(batcher, rx + rw / 3 - 1, ry, 2, rh, COR_DIVISAO);
            Ret(batcher, rx + rw * 2 / 3 - 1, ry, 2, rh, COR_DIVISAO);
            Borda(batcher, rx - 1, ry - 1, rw + 2, rh + 2, COR_BORDA2);

            if (b.Morreu)
            {
                // pisca suave enquanto some
                float t = (Time.Ticks - b.MorreuEm) / (float)MORTO_MS;
                b.TxtMorto.Alpha = t < 0.7f ? 1f : Math.Max(0f, 1f - (t - 0.7f) / 0.3f);
                b.TxtMorto.Draw(batcher, rx, ry - 1);

                return;
            }

            // porcentagem no meio da barra
            int pct = (int)Math.Ceiling(b.Vida / 10f);

            if (b.TxtPct == null || pct != b.UltimoPct)
            {
                b.UltimoPct = pct;
                b.TxtPct?.Dispose();
                b.TxtPct = TextBox.GetOne(pct + "%", EmbeddedFontNames.ROBOTO_BOLD, 13, Color.White, TextBox.RTLOptions.DefaultCenterStroked(rw));
            }

            b.TxtPct.Draw(batcher, rx, ry + 2);
        }

        // ---------- clique no X ----------
        public override void OnMouseUp(int x, int y, MouseButtonType button)
        {
            if (button == MouseButtonType.Left)
            {
                int i = y / (ALT + ESPACO);
                int dentro = y - i * (ALT + ESPACO);

                if (i >= 0 && i < _barras.Count && dentro >= 7 && dentro < 7 + X_TAM && x >= LARG - 8 - X_TAM && x < LARG - 8)
                {
                    Barra b = _barras[i];

                    if (!b.Morreu)
                    {
                        _ignorados.Add(b.Uid);   // ignora ate ele morrer
                    }

                    Remover(b);

                    return;
                }
            }

            base.OnMouseUp(x, y, button);
        }

        protected override void OnDragEnd(int x, int y)
        {
            base.OnDragEnd(x, y);
            Salvar();
        }

        // ---------- posicao salva no perfil ----------
        private static string ArquivoPosicao()
        {
            return string.IsNullOrEmpty(ProfileManager.ProfilePath) ? null : Path.Combine(ProfileManager.ProfilePath, "adventure_boss.txt");
        }

        private void CarregarPosicao()
        {
            X = 420;
            Y = 40;

            try
            {
                string arq = ArquivoPosicao();

                if (arq != null && File.Exists(arq))
                {
                    string[] p = File.ReadAllText(arq).Split(' ');

                    if (p.Length >= 2 && int.TryParse(p[0], out int px) && int.TryParse(p[1], out int py))
                    {
                        X = px;
                        Y = py;
                    }
                }
            }
            catch
            {
                // sem arquivo: posicao padrao (topo da tela)
            }
        }

        private void Salvar()
        {
            try
            {
                string arq = ArquivoPosicao();

                if (arq != null)
                {
                    File.WriteAllText(arq, X + " " + Y);
                }
            }
            catch
            {
                // nao conseguiu salvar: sem problema
            }
        }

        public override void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            Salvar();

            foreach (Barra b in _barras.ToArray())
            {
                b.TxtNome?.Dispose();
                b.TxtNivel?.Dispose();
                b.TxtPct?.Dispose();
                b.TxtMorto?.Dispose();
                b.TxtX?.Dispose();
            }

            _barras.Clear();

            if (_instancia == this)
            {
                _instancia = null;
            }

            base.Dispose();
        }
    }
}
