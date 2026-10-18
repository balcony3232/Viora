-- Viora Online Accounts: run in a NEW Supabase project's SQL editor.
-- Usernames and public IDs are global. Passwords/email remain in Supabase Auth.
-- No service-role key ever belongs in the Viora desktop application.
-- A unique username is claimed only after verified email sign-in, so
-- unverified signups cannot reserve names indefinitely.
begin;

create table public.viora_profiles (
    id uuid primary key references auth.users(id) on delete cascade,
    public_id bigint generated always as identity unique,
    username text not null check (username ~ '^[A-Za-z0-9_]{3,24}$'),
    role text not null default 'member' check (role in ('member','developer','administrator')),
    joined_at timestamptz not null default now(),
    leaderboard_opt_in boolean not null default false
);
create unique index viora_profiles_username_ci on public.viora_profiles (lower(username));

create table public.viora_playtime (
    id uuid primary key references public.viora_profiles(id) on delete cascade,
    seconds bigint not null default 0 check (seconds >= 0),
    last_ping timestamptz,
    last_ping_playing boolean not null default false
);

alter table public.viora_profiles enable row level security;
alter table public.viora_playtime enable row level security;

-- No direct client table reads or writes. Carefully reviewed RPCs expose
-- only a caller's own account or opted-in leaderboard data.
revoke all on public.viora_profiles from public, anon, authenticated;
revoke all on public.viora_playtime from public, anon, authenticated;
revoke all on sequence public.viora_profiles_public_id_seq from public, anon, authenticated;

-- A verified Supabase Auth user can claim a unique username once.
-- Never allow arbitrary profile metadata or direct client INSERTs to assign roles.
create or replace function public.viora_claim_username(p_username text)
returns table(public_id bigint, username text, role text,
              joined_at timestamptz, leaderboard_opt_in boolean, playtime_seconds bigint)
language plpgsql volatile
security definer
set search_path = ''
as $$
declare
    user_id uuid := (select auth.uid());
    username_clean text := trim(coalesce(p_username, ''));
begin
    if user_id is null then
        raise exception 'Sign in and verify your email first' using errcode = '28000';
    end if;
    if username_clean !~ '^[A-Za-z0-9_]{3,24}$' then
        raise exception 'Username must be 3-24 letters, numbers or underscores'
            using errcode = '22023';
    end if;
    if not exists(select 1 from auth.users u
                  where u.id = user_id and u.email_confirmed_at is not null) then
        raise exception 'Email confirmation is required' using errcode = '28000';
    end if;
    insert into public.viora_profiles(id, username)
        values (user_id, username_clean)
        on conflict (id) do nothing;
    insert into public.viora_playtime(id) values (user_id)
        on conflict (id) do nothing;
    return query
        select p.public_id, p.username, p.role, p.joined_at,
               p.leaderboard_opt_in, t.seconds
        from public.viora_profiles p
        join public.viora_playtime t on t.id = p.id
        where p.id = user_id;
end;
$$;

-- Intentionally no email, password, provider metadata, or JWT secrets in results.
create or replace function public.viora_me()
returns table(public_id bigint, username text, role text, joined_at timestamptz,
              leaderboard_opt_in boolean, playtime_seconds bigint)
language sql stable
security definer
set search_path = ''
as $$
    select p.public_id, p.username, p.role, p.joined_at,
           p.leaderboard_opt_in, t.seconds
    from public.viora_profiles p
    join public.viora_playtime t on t.id = p.id
    where p.id = (select auth.uid())
$$;

create or replace function public.viora_leaderboard(p_limit integer default 50)
returns table(rank bigint, public_id bigint, username text, role text,
              playtime_seconds bigint)
language sql stable
security definer
set search_path = ''
as $$
    select row_number() over (order by t.seconds desc, p.public_id asc) as rank,
           p.public_id, p.username, p.role, t.seconds
    from public.viora_profiles p
    join public.viora_playtime t on t.id = p.id
    where p.leaderboard_opt_in = true
    order by t.seconds desc, p.public_id asc
    limit least(greatest(coalesce(p_limit, 50), 1), 100)
$$;

create or replace function public.viora_set_leaderboard_visibility(p_visible boolean)
returns boolean
language plpgsql volatile
security definer
set search_path = ''
as $$
begin
    if (select auth.uid()) is null or p_visible is null then
        raise exception 'Authentication required' using errcode = '28000';
    end if;
    update public.viora_profiles set leaderboard_opt_in = p_visible
    where id = (select auth.uid());
    if not p_visible then
        update public.viora_playtime
        set last_ping = null, last_ping_playing = false
        where id = (select auth.uid());
    end if;
    return found;
end;
$$;

-- A client can lie about Roblox being open. This bounds *how quickly* it can
-- accrue time but is NOT anti-cheat proof. Never award client-supplied seconds.
-- First playing ping starts an interval; only back-to-back playing pings with
-- 20–95 seconds elapsed are credited, capped to 65 seconds per update.
create or replace function public.viora_playtime_ping(p_playing boolean)
returns bigint
language plpgsql volatile
security definer
set search_path = ''
as $$
declare
    player_id uuid := (select auth.uid());
    previous timestamptz;
    previously_playing boolean;
    current_seconds bigint;
    delta double precision;
    moment timestamptz := clock_timestamp();
begin
    if player_id is null or p_playing is null then
        raise exception 'Authentication required' using errcode = '28000';
    end if;

    -- Only opted-in players upload time. Hidden accounts do not advance.
    if not exists (select 1 from public.viora_profiles
                   where id = player_id and leaderboard_opt_in) then
        raise exception 'Leaderboard participation is disabled'
            using errcode = '42501';
    end if;
    select last_ping, last_ping_playing into previous, previously_playing
    from public.viora_playtime where id = player_id for update;
    if not found then raise exception 'Viora profile missing' using errcode = 'P0002'; end if;
    delta := extract(epoch from (moment - previous));
    if p_playing and previously_playing and delta >= 20 and delta <= 95 then
        update public.viora_playtime
        set seconds = seconds + least(floor(delta)::bigint, 65),
            last_ping = moment, last_ping_playing = true
        where id = player_id returning seconds into current_seconds;
    else
        update public.viora_playtime
        set last_ping = moment, last_ping_playing = p_playing
        where id = player_id returning seconds into current_seconds;
    end if;
    return current_seconds;
end;
$$;

revoke all on function public.viora_claim_username(text) from public, anon, authenticated;
revoke all on function public.viora_me() from public, anon, authenticated;
revoke all on function public.viora_leaderboard(integer) from public, anon, authenticated;
revoke all on function public.viora_set_leaderboard_visibility(boolean) from public, anon, authenticated;
revoke all on function public.viora_playtime_ping(boolean) from public, anon, authenticated;
grant execute on function public.viora_claim_username(text) to authenticated;
grant execute on function public.viora_me() to authenticated;
grant execute on function public.viora_leaderboard(integer) to authenticated;
grant execute on function public.viora_set_leaderboard_visibility(boolean) to authenticated;
grant execute on function public.viora_playtime_ping(boolean) to authenticated;

commit;

-- Assign verified roles ONLY in the Supabase SQL editor / trusted server:
-- update public.viora_profiles set role='developer'
-- where id = '<UUID FROM AUTH DASHBOARD>';
-- The public desktop key cannot execute this update.
