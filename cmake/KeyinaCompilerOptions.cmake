include_guard(GLOBAL)

option(KEYINA_WARNINGS_AS_ERRORS "Treat compiler warnings as errors" ON)
option(KEYINA_ENABLE_SANITIZERS "Enable address and undefined-behavior sanitizers" OFF)

function(keyina_configure_target target)
  if(MSVC)
    target_compile_options(${target} PRIVATE
      /W4
      /permissive-
      /utf-8
      /FS
      /EHsc
      /FS
      /Zc:__cplusplus
      $<$<BOOL:${KEYINA_WARNINGS_AS_ERRORS}>:/WX>
      $<$<BOOL:${KEYINA_ENABLE_SANITIZERS}>:/fsanitize=address>
    )
    if(KEYINA_ENABLE_SANITIZERS)
      target_link_options(${target} PRIVATE /INCREMENTAL:NO)
    endif()
  else()
    target_compile_options(${target} PRIVATE
      -Wall
      -Wextra
      -Wpedantic
      $<$<BOOL:${KEYINA_WARNINGS_AS_ERRORS}>:-Werror>
    )

    if(KEYINA_ENABLE_SANITIZERS)
      target_compile_options(${target} PRIVATE
        -fno-omit-frame-pointer
        -fsanitize=address,undefined
      )
      target_link_options(${target} PRIVATE
        -fno-omit-frame-pointer
        -fsanitize=address,undefined
      )
    endif()
  endif()
endfunction()
