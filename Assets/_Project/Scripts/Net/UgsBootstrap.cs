using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

namespace NightOffice
{
    /// <summary>Unity Gaming Services init + anonymous sign-in (one profile per MPPM instance).</summary>
    public static class UgsBootstrap
    {
        static Task<bool> s_Task;

        public static bool Ready { get; private set; }
        public static string Error { get; private set; }
        public static string PlayerId { get; private set; }
        public static string DisplayName => PlayerPrefs.GetString("player.name", "직원" + InstanceInfo.AuthProfile.GetHashCode() % 90 + 10);

        public static Task<bool> EnsureAsync()
        {
            if (Ready) return Task.FromResult(true);
            return s_Task ??= Init();
        }

        static async Task<bool> Init()
        {
            try
            {
                if (UnityServices.State == ServicesInitializationState.Uninitialized)
                {
                    var options = new InitializationOptions();
                    options.SetProfile(InstanceInfo.AuthProfile);
                    await UnityServices.InitializeAsync(options);
                }
                if (!AuthenticationService.Instance.IsSignedIn)
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                PlayerId = AuthenticationService.Instance.PlayerId;
                Ready = true;
                Error = null;
                GameLog.Info("UGS", $"signed in profile={InstanceInfo.AuthProfile} player={PlayerId}");
                return true;
            }
            catch (Exception e)
            {
                Error = e.Message;
                s_Task = null; // allow retry
                GameLog.Warn("UGS", $"sign-in failed: {e.Message}");
                return false;
            }
        }

        public static string FriendlyError
        {
            get
            {
                if (string.IsNullOrEmpty(Error)) return "";
                if (Error.Contains("project", StringComparison.OrdinalIgnoreCase) || Error.Contains("cloud", StringComparison.OrdinalIgnoreCase))
                    return "Unity Cloud 프로젝트가 연결되지 않았습니다 (Edit > Project Settings > Services).";
                return Error;
            }
        }
    }
}
