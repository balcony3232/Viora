-- Viora Online v1.4: add Tester/Admin/Developer display tags
-- and OWNER-ONLY tag management. Run once AFTER 20261018_viora_online_accounts.sql.
-- Do NOT modify auth.users, user passwords, sessions or Roblox accounts.
begin;

-- The owner's identity is assigned once by a trusted operator in SQL Editor.
-- Neither the desktop app nor any publicly executable RPC can grant "owner".
alter table public.viora_profiles
    drop constraint if exists viora_profiles_role_check;
alter table public.viora_profiles
    add constraint viora_profiles_role_check
    check (role in ('member','tester','developer','administrator','owner'));

create table public.viora_role_audit (
    id bigint generated always as identity primary key,
    -- Drop account identifiers from audit rows when a user deletes their account
    -- so the existing authenticated deletion flow is not blocked by audit FKs.
    actor_id uuid references auth.users(id) on delete set null,
    target_id uuid references auth.users(id) on delete set null,
    previous_role text not null,
    assigned_role text not null,
    changed_at timestamptz not null default now()
);
alter table public.viora_role_audit enable row level security;
revoke all on public.viora_role_audit from public, anon, authenticated;
revoke all on sequence public.viora_role_audit_id_seq from public, anon, authenticated;

-- Lookup requires verified session and a database-managed owner role;
-- searching users does not expose email addresses, UUIDs or other private data.
create or replace function public.viora_owner_lookup(p_username text)
returns table(public_id bigint, username text, role text)
language plpgsql stable security definer
set search_path = ''
as $$
declare
    actor_id uuid := (select auth.uid());
begin
    if actor_id is null or not exists (
        select 1 from public.viora_profiles where id = actor_id and role = 'owner'
    ) then
        raise exception 'Owner permissions required' using errcode = '42501';
    end if;
    if length(trim(coalesce(p_username, ''))) < 3 then
        raise exception 'Enter an exact Viora username' using errcode = '22023';
    end if;
    return query
        select p.public_id, p.username, p.role
        from public.viora_profiles p
        where lower(p.username) = lower(trim(p_username))
        limit 1;
end;
$$;

-- Privilege checks live in the database and run on EVERY request.
-- The owner's own role is protected, and owner status can never be issued here.
create or replace function public.viora_owner_set_role(p_target_public_id bigint, p_new_role text)
returns table(public_id bigint, username text, role text)
language plpgsql volatile security definer
set search_path = ''
as $$
declare
    actor_id uuid := (select auth.uid());
    target_id uuid;
    old_role text;
    desired_role text := lower(trim(coalesce(p_new_role, '')));
begin
    if actor_id is null or not exists (
        select 1 from public.viora_profiles where id = actor_id and role = 'owner'
    ) then
        raise exception 'Owner permissions required' using errcode = '42501';
    end if;
    if desired_role not in ('member', 'tester', 'developer', 'administrator') then
        raise exception 'Unsupported assignable role' using errcode = '22023';
    end if;
    select p.id, p.role into target_id, old_role
    from public.viora_profiles p where p.public_id = p_target_public_id for update;
    if not found then
        raise exception 'Viora user not found' using errcode = 'P0002';
    end if;
    if target_id = actor_id or old_role = 'owner' then
        raise exception 'Owner status cannot be changed here' using errcode = '42501';
    end if;

    if old_role is distinct from desired_role then
        update public.viora_profiles p set role = desired_role where p.id = target_id;
        insert into public.viora_role_audit (actor_id, target_id, previous_role, assigned_role)
        values (actor_id, target_id, old_role, desired_role);
    end if;
    return query
        select p.public_id, p.username, p.role
        from public.viora_profiles p where p.id = target_id;
end;
$$;

revoke all on function public.viora_owner_lookup(text) from public, anon, authenticated;
revoke all on function public.viora_owner_set_role(bigint, text) from public, anon, authenticated;
grant execute on function public.viora_owner_lookup(text) to authenticated;
grant execute on function public.viora_owner_set_role(bigint, text) to authenticated;

commit;

-- BOOTSTRAP IN A SEPARATE, TRUSTED ADMIN SQL QUERY (not in the app):
-- 1. Register and verify YOUR actual Viora Online account.
-- 2. Sign in and finish claiming your unique username.
-- 3. Verify the email AND Auth UUID of the exact account in Supabase Authentication.
-- 4. Execute (substitute the authenticated user's VERIFIED UUID):
--    update public.viora_profiles set role = 'owner'
--    where id = '<VERIFIED_USER_UUID>';
-- 5. Sign out and sign back in. Never grant owner based on username alone.
