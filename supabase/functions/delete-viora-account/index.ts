// Deploy with verify_jwt=true. Privileged credentials exist ONLY in Supabase
// Edge Function environment; never ship them in a Windows binary.
import { createClient } from 'npm:@supabase/supabase-js@2'

Deno.serve(async (request: Request): Promise<Response> => {
  if (request.method !== 'POST')
    return Response.json({ error: 'Method not allowed' }, { status: 405 })

  let body: { confirm?: string }
  try { body = await request.json() }
  catch { return Response.json({ error: 'Invalid request' }, { status: 400 }) }
  if (body.confirm !== 'DELETE')
    return Response.json({ error: 'Explicit confirmation required' }, { status: 400 })

  const authorization = request.headers.get('Authorization') ?? ''
  const bearer = authorization.match(/^Bearer\s+(.+)$/i)?.[1]
  const url = Deno.env.get('SUPABASE_URL')
  const publishableOrAnonKey = Deno.env.get('SUPABASE_ANON_KEY')
  const serviceRole = Deno.env.get('SUPABASE_SERVICE_ROLE_KEY')
  if (!bearer || !url || !publishableOrAnonKey || !serviceRole)
    return Response.json({ error: 'Authentication unavailable' }, { status: 401 })

  // Verify the JWT through Supabase Auth; NEVER accept an arbitrary user UUID.
  const userClient = createClient(url, publishableOrAnonKey, {
    auth: { persistSession: false, autoRefreshToken: false }
  })
  const { data: { user }, error: authError } = await userClient.auth.getUser(bearer)
  if (authError || !user)
    return Response.json({ error: 'Authentication required' }, { status: 401 })

  const admin = createClient(url, serviceRole, {
    auth: { persistSession: false, autoRefreshToken: false }
  })

  // Remove public avatar bytes before deleting the Auth identity. A SQL
  // cascade alone would leave the Storage object publicly accessible.
  const { data: profile, error: profileError } = await admin
    .from('viora_profiles').select('public_id,avatar_version').eq('id', user.id).maybeSingle()
  if (profileError)
    return Response.json({ error: 'Unable to locate account' }, { status: 500 })

  if (profile && profile.avatar_version > 0) {
    const { error: avatarError } = await admin.storage
      .from('viora-avatars').remove([String(profile.public_id) + '.png'])
    if (avatarError)
      return Response.json({ error: 'Avatar deletion failed; account was not deleted' }, { status: 500 })
  }

  const { error } = await admin.auth.admin.deleteUser(user.id)
  if (error)
    return Response.json({ error: 'Unable to delete account' }, { status: 500 })

  return Response.json({ deleted: true })
})
