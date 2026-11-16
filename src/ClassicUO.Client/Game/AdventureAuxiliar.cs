// SPDX-License-Identifier: BSD-2-Clause
// ==========================================================================
// ADVENTURE: TECLAS DO AUXILIAR DO SHARD.
//
//  F1..F8        -> o cliente fala (escondido) ".auxb 1" .. ".auxb 8"
//  Shift+F1..F8  -> ".auxb 9" .. ".auxb 16"
//
// - O comando vai como fala para o servidor, mas nao aparece no journal nem em cima do char
//   (o Sphere trata como comando e nao repete o texto).
// - Estas teclas tem PRIORIDADE sobre as macros do TazUO: se a tecla e do auxiliar,
//   a macro que estiver nela nao roda.
// - Com Ctrl ou Alt apertado, a tecla nao e do auxiliar (segue para as macros normais).
// - Segurar a tecla nao repete: um aperto = um comando (e no minimo 150 ms entre um e outro).
// - O que cada numero faz e decidido no servidor (FUNCTION AUXB no Sphere).
// ==========================================================================

using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using ClassicUO.Input;
using ClassicUO.Network;
using SDL3;

namespace ClassicUO.Game
{
    internal static class AdventureAuxiliar
    {
        private const uint INTERVALO_MS = 150;
        private static readonly uint[] _ultimo = new uint[17];

        // Devolve true se a tecla era do auxiliar (e ja foi tratada): ai nenhuma macro roda nela
        public static bool TeclaApertada(SDL.SDL_Keycode key, bool repetindo)
        {
            if (Keyboard.Ctrl || Keyboard.Alt)
            {
                return false;
            }

            int n;

            switch (key)
            {
                case SDL.SDL_Keycode.SDLK_F1: n = 1; break;
                case SDL.SDL_Keycode.SDLK_F2: n = 2; break;
                case SDL.SDL_Keycode.SDLK_F3: n = 3; break;
                case SDL.SDL_Keycode.SDLK_F4: n = 4; break;
                case SDL.SDL_Keycode.SDLK_F5: n = 5; break;
                case SDL.SDL_Keycode.SDLK_F6: n = 6; break;
                case SDL.SDL_Keycode.SDLK_F7: n = 7; break;
                case SDL.SDL_Keycode.SDLK_F8: n = 8; break;
                default: return false;
            }

            if (Keyboard.Shift)
            {
                n += 8;   // Shift+F1..F8 = 9..16
            }

            if (repetindo || Time.Ticks - _ultimo[n] < INTERVALO_MS)
            {
                return true;   // tecla segurada ou rapido demais: ignora, mas continua sendo do auxiliar
            }

            _ultimo[n] = Time.Ticks;

            AsyncNetClient.Socket.Send_UnicodeSpeechRequest(
                ".auxb " + n,
                MessageType.Regular,
                3,
                0x03B2,
                Settings.GlobalSettings.Language
            );

            return true;
        }
    }
}
