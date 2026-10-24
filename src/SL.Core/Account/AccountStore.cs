using System.IO;
using System.Text.Json;

namespace SL.Core.Account;

/// <summary>一条账户记录:离线账户只有名字;正版账户内嵌完整微软会话。</summary>
public sealed record AccountEntry
{
    /// <summary>稳定 Id:"msa:&lt;uuid&gt;" 或 "offline:&lt;玩家名&gt;"。</summary>
    public string Id { get; init; } = "";
    /// <summary>"offline"(离线)或 "msa"(微软正版)。</summary>
    public string Kind { get; init; } = "offline";
    public string Name { get; init; } = "";
    /// <summary>正版账户的完整会话(含 refresh_token);离线为 null。</summary>
    public MsSession? Session { get; init; }
    public DateTimeOffset SavedAt { get; init; } = DateTimeOffset.Now;
}

/// <summary>accounts\accounts.json 的仓库形态:账户集合 + 活动账户 Id。</summary>
public sealed record AccountRepository
{
    public List<AccountEntry> Accounts { get; init; } = [];
    public string ActiveId { get; init; } = "";
}

/// <summary>
/// 多账户仓库:正版(可多个微软账户)与离线账户混合存储,支持添加/更新/删除/切换。
/// 旧版单会话 msa.json 在首次读取时自动迁移为仓库中的一条正版账户。
/// </summary>
public static class AccountStore
{
    private static readonly object Gate = new();

    public static AccountRepository Load()
    {
        lock (Gate)
        {
            try
            {
                var path = RepositoryPath();
                if (!File.Exists(path))
                {
                    // 迁移:旧版只有 accounts\msa.json 单会话,转成仓库的第一条正版账户
                    var legacy = MsAuth.LoadSession();
                    if (legacy is { Uuid.Length: > 0 })
                    {
                        var migrated = new AccountRepository
                        {
                            Accounts =
                            [
                                new AccountEntry
                                {
                                    Id = "msa:" + legacy.Uuid,
                                    Kind = "msa",
                                    Name = legacy.Name,
                                    Session = legacy,
                                },
                            ],
                            ActiveId = "msa:" + legacy.Uuid,
                        };
                        SaveCore(migrated);
                        return migrated;
                    }
                    return new AccountRepository();
                }
                return JsonSerializer.Deserialize<AccountRepository>(File.ReadAllText(path)) ?? new AccountRepository();
            }
            catch (Exception)
            {
                return new AccountRepository();
            }
        }
    }

    /// <summary>新增或按 Id 更新正版账户并设为活动。</summary>
    public static AccountEntry UpsertMsa(MsSession session)
    {
        lock (Gate)
        {
            var repo = Load();
            var id = "msa:" + session.Uuid;
            var entry = new AccountEntry
            {
                Id = id,
                Kind = "msa",
                Name = session.Name,
                Session = session,
                SavedAt = DateTimeOffset.Now,
            };
            repo.Accounts.RemoveAll(a => a.Id == id);
            repo.Accounts.Insert(0, entry);
            SaveCore(repo with { ActiveId = id });
            return entry;
        }
    }

    /// <summary>添加(或选中已存在的)离线账户并设为活动。</summary>
    public static AccountEntry AddOffline(string name)
    {
        lock (Gate)
        {
            var clean = name.Trim();
            var repo = Load();
            var id = "offline:" + clean;
            var exists = repo.Accounts.FirstOrDefault(a => a.Id == id);
            if (exists is not null)
            {
                SaveCore(repo with { ActiveId = id });
                return exists;
            }
            var entry = new AccountEntry { Id = id, Kind = "offline", Name = clean };
            repo.Accounts.Add(entry);
            SaveCore(repo with { ActiveId = id });
            return entry;
        }
    }

    /// <summary>删除账户;若删的是活动账户,活动位顺延到剩余第一个(可能为空)。</summary>
    public static void Remove(string id)
    {
        lock (Gate)
        {
            var repo = Load();
            repo.Accounts.RemoveAll(a => a.Id == id);
            var activeId = repo.ActiveId == id ? repo.Accounts.FirstOrDefault()?.Id ?? "" : repo.ActiveId;
            SaveCore(repo with { ActiveId = activeId });
        }
    }

    public static void SetActive(string id)
    {
        lock (Gate)
        {
            var repo = Load();
            if (repo.Accounts.All(a => a.Id != id))
                return;
            SaveCore(repo with { ActiveId = id });
        }
    }

    /// <summary>当前活动账户;仓库为空时返回 null(调用方回退离线名)。</summary>
    public static AccountEntry? Active()
    {
        var repo = Load();
        return repo.Accounts.FirstOrDefault(a => a.Id == repo.ActiveId);
    }

    public static string RepositoryPath() => Path.Combine(Boot.Runtime.Root, "accounts", "accounts.json");

    private static void SaveCore(AccountRepository repo)
    {
        var path = RepositoryPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(repo, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, path, overwrite: true);
    }
}
