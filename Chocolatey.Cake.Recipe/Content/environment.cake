// Copyright © 2022 Chocolatey Software, Inc
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
// You may obtain a copy of the License at
//
// 	http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

public static class Environment
{
    public static string DefaultPushSourceUrlVariable { get; private set; }
    public static string GitHubTokenVariable { get; private set; }
    public static string TransifexApiTokenVariable { get; private set; }

    public static void SetVariableNames(
        string defaultPushSourceUrlVariable = null,
        string gitHubTokenVariable = null,
        string transifexApiTokenVariable = null)
    {
        DefaultPushSourceUrlVariable = defaultPushSourceUrlVariable ?? "NUGETDEVPUSH_SOURCE";
        GitHubTokenVariable = gitHubTokenVariable ?? "GITHUB_PAT";
        TransifexApiTokenVariable = transifexApiTokenVariable ?? "TRANSIFEX_API_TOKEN";
    }
}