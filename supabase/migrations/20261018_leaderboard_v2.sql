-- Viora Leaderboard v2. Run ONCE after base accounts/Owner migration.
-- Private by default: no one is added to the public leaderboard automatically.
begin;

alter table public.viora_profiles
  add column if not exists avatar_version integer not null default 0
    check (avatar_version >= 0),
  add column if not exists leaderboard_consent_seen boolean not null default false,
  add column if not exists avatar_visible boolean not null default false;

-- A one-time explicit choice (yes/no) is stored server-side, even if declined.
create or replace function public.viora_leaderboard_consent()
returns boolean
language sql stable security definer set search_path = ''
as $$
  select p.leaderboard_consent_seen
  from public.viora_profiles p where p.id = (select auth.uid())
$$;

create or replace function public.viora_set_leaderboard_visibility(p_visible boolean)
returns boolean
language plpgsql volatile security definer set search_path = ''
as $$
begin
    if (select auth.uid()) is null or p_visible is null then
        raise exception 'Authentication required' using errcode = '28000';
    end if;
    update public.viora_profiles
    set leaderboard_opt_in = p_visible, leaderboard_consent_seen = true
    where id = (select auth.uid());
    if not p_visible then
        update public.viora_playtime
        set last_ping = null, last_ping_playing = false
        where id = (select auth.uid());
    end if;
    return found;
end;
$$;

-- RPC v2 keeps the original API intact for older Viora builds.
-- Avatar version is visible ONLY when its owner participates publicly.
create function public.viora_leaderboard_v2(p_limit integer default 100)
returns table(rank bigint, public_id bigint, username text, role text,
              playtime_seconds bigint, avatar_version integer)
language sql stable security definer set search_path = ''
as $$
  select row_number() over (order by t.seconds desc, p.public_id asc) as rank,
         p.public_id, p.username, p.role, t.seconds, case when p.avatar_visible then p.avatar_version else 0 end
  from public.viora_profiles p
  join public.viora_playtime t on t.id = p.id
  where p.leaderboard_opt_in = true
  order by t.seconds desc, p.public_id asc
  limit least(greatest(coalesce(p_limit,100),1),100)
$$;

-- Called only by the signed-in player, including when rank > 100.
-- No other users' data or private emails/UUIDs are exposed.
create function public.viora_my_leaderboard_rank()
returns table(rank bigint, public_id bigint, username text, role text,
              playtime_seconds bigint, avatar_version integer, opted_in boolean)
language sql stable security definer set search_path = ''
as $$
  with ranked as (
    select p.id,
           row_number() over (order by t.seconds desc,p.public_id asc) as rank
    from public.viora_profiles p
    join public.viora_playtime t on t.id=p.id
    where p.leaderboard_opt_in = true
  )
  select case when p.leaderboard_opt_in then r.rank else null::bigint end,
         p.public_id, p.username, p.role, t.seconds,
         case when p.avatar_visible then p.avatar_version else 0 end,
         p.leaderboard_opt_in
  from public.viora_profiles p
  join public.viora_playtime t on t.id=p.id
  left join ranked r on r.id=p.id
  where p.id=(select auth.uid())
$$;

-- Avatars are optional; images are re-encoded as small PNGs by Viora.
-- Stored as <publicID>.png to avoid exposing auth UUIDs in image URLs.
-- Files in this bucket are publicly readable: opt-out removes avatar paths
-- from leaderboard RPC results but old image URLs may remain accessible.
-- Users explicitly consent when uploading. No arbitrary URL upload allowed.
insert into storage.buckets (id, name, public, file_size_limit, allowed_mime_types)
values ('viora-avatars','viora-avatars',true,262144,array['image/png'])
on conflict (id) do nothing;

-- RLS uses a SECURITY DEFINER ownership check: clients cannot query the
-- private profiles table or overwrite another account's avatar.
create function public.viora_owns_avatar(p_name text)
returns boolean
language sql stable security definer set search_path = ''
as $$
    select exists(
      select 1 from public.viora_profiles p
      where p.id=(select auth.uid())
        and p_name = p.public_id::text || '.png'
    )
$$;

create policy "viora avatars insert own" on storage.objects
  for insert to authenticated
  with check (bucket_id='viora-avatars' and (select public.viora_owns_avatar(name)));
create policy "viora avatars update own" on storage.objects
  for update to authenticated
  using (bucket_id='viora-avatars' and (select public.viora_owns_avatar(name)))
  with check (bucket_id='viora-avatars' and (select public.viora_owns_avatar(name)));
create policy "viora avatars delete own" on storage.objects
  for delete to authenticated
  using (bucket_id='viora-avatars' and (select public.viora_owns_avatar(name)));
create policy "viora avatars select own for update" on storage.objects
  for select to authenticated
  using (bucket_id='viora-avatars' and (select public.viora_owns_avatar(name)));

-- Separate version bump after a successful upload; the public leaderboard
-- never shows avatars until upload and version bump succeed.
create function public.viora_avatar_uploaded()
returns integer
language plpgsql volatile security definer set search_path = ''
as $$
declare
    new_version integer;
begin
    if (select auth.uid()) is null then
        raise exception 'Authentication required' using errcode = '28000';
    end if;
    -- Server-side existence check prevents claiming an avatar upload that
    -- never happened. Public ID is owned by the authenticated user.
    if not exists (
      select 1 from storage.objects o
      join public.viora_profiles p on o.name = p.public_id::text || '.png'
      where p.id=(select auth.uid()) and o.bucket_id='viora-avatars'
    ) then
      raise exception 'Avatar upload not found' using errcode='P0002';
    end if;
    update public.viora_profiles p
    set avatar_version = p.avatar_version + 1, avatar_visible = true
    where p.id=(select auth.uid())
    returning p.avatar_version into new_version;
    return new_version;
end;
$$;

-- Only called after the authenticated client removes its own Storage object.
create function public.viora_avatar_removed()
returns integer
language plpgsql volatile security definer set search_path = ''
as $
declare
    new_version integer;
begin
    if (select auth.uid()) is null then
        raise exception 'Authentication required' using errcode='28000';
    end if;
    -- Do not report removed while image bytes are still present.
    if exists(
      select 1 from storage.objects o
      join public.viora_profiles p on o.name = p.public_id::text || '.png'
      where p.id=(select auth.uid()) and o.bucket_id='viora-avatars'
    ) then
      raise exception 'Avatar file must be deleted first' using errcode='23514';
    end if;
    update public.viora_profiles p
    set avatar_visible=false, avatar_version=p.avatar_version+1
    where p.id=(select auth.uid())
    returning p.avatar_version into new_version;
    return new_version;
end;
$;

revoke all on function public.viora_leaderboard_consent() from public,anon,authenticated;
revoke all on function public.viora_leaderboard_v2(integer) from public,anon,authenticated;
revoke all on function public.viora_my_leaderboard_rank() from public,anon,authenticated;
revoke all on function public.viora_owns_avatar(text) from public,anon,authenticated;
revoke all on function public.viora_avatar_uploaded() from public,anon,authenticated;
revoke all on function public.viora_avatar_removed() from public,anon,authenticated;
grant execute on function public.viora_leaderboard_consent() to authenticated;
grant execute on function public.viora_leaderboard_v2(integer) to authenticated;
grant execute on function public.viora_my_leaderboard_rank() to authenticated;
grant execute on function public.viora_avatar_uploaded() to authenticated;
grant execute on function public.viora_avatar_removed() to authenticated;
-- For RLS policy evaluation, authenticated must execute this narrow boolean check.
grant execute on function public.viora_owns_avatar(text) to authenticated;

commit;
