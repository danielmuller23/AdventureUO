// SPDX-License-Identifier: BSD-2-Clause
// ==========================================================================
// ADVENTURE: TECLAS LIVRES DO AUXILIAR DO SHARD.
//
// O SERVIDOR e a fonte de verdade: ele diz qual tecla vai em qual botao.
//
// 1) ESCOLHER TECLA. Servidor manda 0xBF sub 0xAD08 (6 bytes): [botao byte]
//      Sphere: SENDPACKET 0bf w06 w0AD08 <botao>
//    O cliente mostra "aperte a tecla..." e espera a PROXIMA tecla ou botao do mouse
//    (rodinha cima/baixo, botao do meio, Mouse4, Mouse5), com Ctrl/Alt/Shift se tiver.
//    Responde em fala oculta:  .auxtecla N,NomeDaTecla     (ex.: .auxtecla 3,Ctrl+F1)
//      Esc    = cancela sem mandar nada
//      Delete = .auxtecla N,      (vazio = apagar a tecla do botao N)
//    Botao esquerdo e direito do mouse NAO sao capturados (sao do jogo).
//    Sem resposta em 20 s, cancela sozinho.
//
// 2) LIGAR TECLA. Mensagem de sistema na COR 0x0AD7 (nao vai para o journal):
//      AUXK|N|Tecla   liga a tecla ao botao N
//      AUXK|N|        desliga o botao N
//      AUXK|0|        desliga todos
//    Sphere: SRC.SYSMESSAGE @0AD7 AUXK|3|Ctrl+F1
//
// 3) USAR. Ao apertar uma tecla ligada o cliente manda o pacote 0xBF sub 0xAD10 (6 bytes):
//      [0xBF][0x0006][0xAD10][botao byte]
//    Tem prioridade sobre as macros do TazUO. Segurar a tecla nao repete.
//
// Botoes de 1 a 24 (AUXK, 0xAD08 e 0xAD10).
//
// Nomes das teclas: modificadores na ordem Ctrl+Alt+Shift+ e depois a tecla
//   (nome do SDL sem "SDLK_": F1, A, 1, SPACE, KP_1...) ou do mouse:
//   BotaoMeio, Mouse4, Mouse5, RodinhaCima, RodinhaBaixo.
// ==========================================================================

using System;
using System.Collections.Generic;
using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.Gumps;
using ClassicUO.Input;
using ClassicUO.Network;
using SDL3;

namespace ClassicUO.Game
{
    internal static class AdventureAuxiliar
    {
        public const ushort COR_TECLA = 0x0AD7;   // cor da mensagem AUXK|N|Tecla

        private const uint INTERVALO_MS = 150;     // minimo entre dois usos da mesma tecla
        private const uint CAPTURA_MS = 20000;     // tempo maximo esperando a tecla
        public const int MAX_BOTOES = 24;           // botoes de 1 a 24

        // tecla (nome normalizado, MAIUSCULO) -> botao N
        private static readonly Dictionary<string, int> _teclas = new Dictionary<string, int>();
        // botao N -> tecla (para trocar a tecla de um botao)
        private static readonly Dictionary<int, string> _botoes = new Dictionary<int, string>();
        private static readonly Dictionary<int, uint> _ultimoUso = new Dictionary<int, uint>();

        public static bool Capturando => _capturaBotao > 0 && Time.Ticks < _capturaAte;
        private static int _capturaBotao;
        private static uint _capturaAte;

        // ------------------------------------------------------------------
        // 1) ESCOLHER TECLA
        // ------------------------------------------------------------------
        public static void IniciarCaptura(World world, int botao)
        {
            if (botao < 1 || botao > MAX_BOTOES)
            {
                return;   // fora de 1..24: ignora
            }

            _capturaBotao = botao;
            _capturaAte = Time.Ticks + CAPTURA_MS;
            AdventureAuxTeclaGump.Mostrar(world, botao);
        }

        public static void CancelarCaptura()
        {
            _capturaBotao = 0;
            AdventureAuxTeclaGump.Fechar();
        }

        // Tecla do teclado durante a captura (chamado pelo GameController, antes de tudo)
        public static void CapturarTecla(SDL.SDL_Keycode key)
        {
            if (!Capturando)
            {
                CancelarCaptura();

                return;
            }

            switch (key)
            {
                case SDL.SDL_Keycode.SDLK_LCTRL:
                case SDL.SDL_Keycode.SDLK_RCTRL:
                case SDL.SDL_Keycode.SDLK_LALT:
                case SDL.SDL_Keycode.SDLK_RALT:
                case SDL.SDL_Keycode.SDLK_LSHIFT:
                case SDL.SDL_Keycode.SDLK_RSHIFT:
                case SDL.SDL_Keycode.SDLK_LGUI:
                case SDL.SDL_Keycode.SDLK_RGUI:
                    return;   // so o modificador: continua esperando a tecla de verdade

                case SDL.SDL_Keycode.SDLK_ESCAPE:
                    CancelarCaptura();   // cancela sem mandar nada

                    return;

                case SDL.SDL_Keycode.SDLK_DELETE:
                    Enviar(".auxtecla " + _capturaBotao + ",");   // vazio = apagar
                    CancelarCaptura();

                    return;
            }

            Enviar(".auxtecla " + _capturaBotao + "," + Modificadores() + NomeTecla(key));
            CancelarCaptura();
        }

        // Botao do mouse ou rodinha durante a captura (chamado pelo GameScene). Devolve true se usou.
        public static bool CapturarMouse(string nomeMouse)
        {
            if (!Capturando)
            {
                return false;
            }

            Enviar(".auxtecla " + _capturaBotao + "," + Modificadores() + nomeMouse);
            CancelarCaptura();

            return true;
        }

        // ------------------------------------------------------------------
        // 2) LIGAR TECLA (mensagem AUXK|N|Tecla na cor 0x0AD7)
        // ------------------------------------------------------------------
        public static void ReceberMensagem(string texto)
        {
            if (string.IsNullOrEmpty(texto))
            {
                return;
            }

            string[] p = texto.Split('|', 3);

            if (p.Length < 3 || !p[0].Trim().Equals("AUXK", StringComparison.OrdinalIgnoreCase) || !int.TryParse(p[1].Trim(), out int n))
            {
                return;   // formato errado: ignora (e nao vai para o journal)
            }

            if (n < 0 || n > MAX_BOTOES)
            {
                return;   // so 0 (apagar todas) ou 1..24
            }

            string tecla = Normalizar(p[2]);

            if (n == 0)
            {
                _teclas.Clear();   // AUXK|0| = desliga todos
                _botoes.Clear();

                return;
            }

            // tira a tecla antiga deste botao
            if (_botoes.TryGetValue(n, out string antiga))
            {
                _teclas.Remove(antiga);
                _botoes.Remove(n);
            }

            if (tecla.Length == 0)
            {
                return;   // AUXK|N| = so desliga o botao N
            }

            // se a tecla estava em outro botao, sai de la
            if (_teclas.TryGetValue(tecla, out int outro))
            {
                _botoes.Remove(outro);
            }

            _teclas[tecla] = n;
            _botoes[n] = tecla;
        }

        // ------------------------------------------------------------------
        // 3) USAR
        // ------------------------------------------------------------------
        // Teclado (GameScene.OnKeyDown, antes das macros). Devolve true se a tecla e do auxiliar.
        public static bool TeclaApertada(SDL.SDL_Keycode key, bool repetindo)
        {
            if (_teclas.Count == 0)
            {
                return false;
            }

            if (!_teclas.TryGetValue(Modificadores().ToUpperInvariant() + NomeTecla(key), out int n))
            {
                return false;
            }

            if (!repetindo)
            {
                Usar(n);
            }

            return true;   // e do auxiliar: nenhuma macro roda nela
        }

        // Mouse (botao do meio, Mouse4, Mouse5, rodinha). Devolve true se e do auxiliar.
        public static bool MouseApertado(string nomeMouse)
        {
            if (_teclas.Count == 0)
            {
                return false;
            }

            if (!_teclas.TryGetValue(Modificadores().ToUpperInvariant() + nomeMouse.ToUpperInvariant(), out int n))
            {
                return false;
            }

            Usar(n);

            return true;
        }

        private static void Usar(int n)
        {
            if (_ultimoUso.TryGetValue(n, out uint ultimo) && Time.Ticks - ultimo < INTERVALO_MS)
            {
                return;   // rapido demais (rodinha girando, por exemplo)
            }

            _ultimoUso[n] = Time.Ticks;
            EnviarBotao(n);
        }

        // Pacote 0xBF sub 0xAD10 (6 bytes): o servidor executa o botao N
        private static void EnviarBotao(int n)
        {
            Span<byte> pacote = stackalloc byte[6];
            pacote[0] = 0xBF;          // pacote estendido
            pacote[1] = 0x00;          // tamanho (2 bytes) = 6
            pacote[2] = 0x06;
            pacote[3] = 0xAD;          // subcomando 0xAD10
            pacote[4] = 0x10;
            pacote[5] = (byte)n;       // botao 1..24

            AsyncNetClient.Socket.Send(pacote);
        }

        // ------------------------------------------------------------------
        // nomes e envio
        // ------------------------------------------------------------------
        public static string NomeMouse(MouseButtonType b)
        {
            switch (b)
            {
                case MouseButtonType.Middle: return "BotaoMeio";
                case MouseButtonType.XButton1: return "Mouse4";
                case MouseButtonType.XButton2: return "Mouse5";
                default: return null;
            }
        }

        private static string Modificadores()
        {
            string s = "";

            if (Keyboard.Ctrl) s += "Ctrl+";
            if (Keyboard.Alt) s += "Alt+";
            if (Keyboard.Shift) s += "Shift+";

            return s;
        }

        private static string NomeTecla(SDL.SDL_Keycode key)
        {
            string s = key.ToString();

            if (s.StartsWith("SDLK_", StringComparison.OrdinalIgnoreCase))
            {
                s = s.Substring(5);
            }

            return s.ToUpperInvariant();
        }

        // "ctrl + f1" -> "CTRL+F1" (modificadores sempre na ordem Ctrl, Alt, Shift)
        private static string Normalizar(string tecla)
        {
            if (string.IsNullOrWhiteSpace(tecla))
            {
                return "";
            }

            bool ctrl = false, alt = false, shift = false;
            string principal = "";

            foreach (string parte in tecla.Split('+'))
            {
                string t = parte.Trim().ToUpperInvariant();

                if (t == "CTRL") ctrl = true;
                else if (t == "ALT") alt = true;
                else if (t == "SHIFT") shift = true;
                else if (t.Length > 0) principal = t.StartsWith("SDLK_") ? t.Substring(5) : t;
            }

            if (principal.Length == 0)
            {
                return "";
            }

            return (ctrl ? "CTRL+" : "") + (alt ? "ALT+" : "") + (shift ? "SHIFT+" : "") + principal;
        }

        private static void Enviar(string comando)
        {
            AsyncNetClient.Socket.Send_UnicodeSpeechRequest(
                comando,
                MessageType.Regular,
                3,
                0x03B2,
                Settings.GlobalSettings.Language
            );
        }

        // Logout / troca de personagem: esquece tudo (o servidor manda de novo no login)
        public static void Limpar()
        {
            _teclas.Clear();
            _botoes.Clear();
            _ultimoUso.Clear();
            CancelarCaptura();
        }
    }
}
